namespace RabbitMQX.Client.MessagePatterns.Unicast.Test {

    using RabbitMQ.Client;
    using RabbitMQ.Client.Exceptions;
    using RabbitMQ.Client.MessagePatterns.Unicast;

    using EndOfStreamException   = System.IO.EndOfStreamException;

    //TODO: this class should live in a separate assembly
    public class TestHelper {

        public static void Sent(IMessage m) {
            LogMessage("sent", m.From, m);
        }

        public static void LogMessage(string action,
                                      string actor,
                                      IMessage m) {
            System.Console.WriteLine("{0} {1} {2}",
                                     actor, action, Decode(m.Body));
        }

        public static void LogMessage(string action,
                                      IMessaging actor,
                                      IMessage m) {
            LogMessage(action, actor.Identity, m);
        }

        public static byte[] Encode(string s) {
            return System.Text.Encoding.UTF8.GetBytes(s);
        }

        public static string Decode(byte[] b) {
            return System.Text.Encoding.UTF8.GetString(b);
        }

    }

    public delegate SetupDelegate MessagingClosure(IMessaging m);

    //NB: For testing we declare all resources as auto-delete and
    //non-durable, to avoid manual cleanup. More typically the
    //resources would be non-auto-delete/non-exclusive and durable, so
    //that they survives server and client restarts.

    public class Tests {

        public static int Main(string[] args) {

            ConnectionFactory factory = new ConnectionFactory();
            AmqpTcpEndpoint server = new AmqpTcpEndpoint();

            TestDirect(factory, server);
            TestRelayed(factory, server);
            TestPreconfigured(factory, server);
            
            return 0;
        }

        protected static void DeclareExchange(IModel m,
                                              string name, string type) {
            m.ExchangeDeclare(name, type,
                              false, false, true, false, false, null);
        }

        protected static void DeclareQueue(IModel m, string name) {
            m.QueueDeclare(name, false, false, false, true, false, null);
        }

        protected static void BindQueue(IModel m,
                                        string q, string x,string rk) {
            m.QueueBind(q, x, rk, false, null);
        }

        protected static void TestDirect(ConnectionFactory factory,
                                         AmqpTcpEndpoint server) {
            using (IConnector conn = Factory.CreateConnector(factory, server)) {
                //create two parties
                IMessaging foo = Factory.CreateMessaging();
                foo.Connector = conn;
                foo.Identity = "foo";
                foo.Sent += TestHelper.Sent;
                (foo as IReceiver).Setup = delegate(IModel channel) {
                    DeclareQueue(channel, foo.Identity);
                };
                foo.Init();
                IMessaging bar = Factory.CreateMessaging();
                bar.Connector = conn;
                bar.Identity = "bar";
                bar.Sent += TestHelper.Sent;
                (bar as IReceiver).Setup = delegate(IModel channel) {
                    DeclareQueue(channel, bar.Identity);
                };
                bar.Init();

                //send message from foo to bar
                IMessage mf = foo.CreateMessage();
                mf.Body = TestHelper.Encode("message1");
                mf.To   = "bar";
                foo.Send(mf);

                //receive message at bar and reply
                IReceivedMessage rb = bar.Receive();
                TestHelper.LogMessage("recv", bar, rb);
                IMessage mb = bar.CreateReply(rb);
                mb.Body = TestHelper.Encode("message2");
                bar.Send(mb);
                bar.Ack(rb);

                //receive reply at foo
                IReceivedMessage rf = foo.Receive();
                TestHelper.LogMessage("recv", foo, rf);
                foo.Ack(rf);
            }
        }

        protected static void TestRelayed(ConnectionFactory factory,
                                          AmqpTcpEndpoint server) {
            MessagingClosure senderSetup = delegate(IMessaging m) {
                return delegate(IModel channel) {
                    DeclareExchange(channel, m.ExchangeName, "fanout");
                };
            };
            MessagingClosure receiverSetup = delegate(IMessaging m) {
                return delegate(IModel channel) {
                    DeclareExchange(channel, "out", "direct");
                    DeclareQueue(channel, m.QueueName);
                    BindQueue(channel, m.QueueName, "out", m.QueueName);
                };
            };
            TestRelayedHelper(senderSetup, receiverSetup, factory, server);
        }

        protected static void TestRelayedHelper(MessagingClosure senderSetup,
                                                MessagingClosure receiverSetup,
                                                ConnectionFactory factory,
                                                AmqpTcpEndpoint server) {
            using (IConnector conn = Factory.CreateConnector(factory, server),
                   relayConn = Factory.CreateConnector(factory, server)) {

                //create relay 
                IMessaging relay = Factory.CreateMessaging();
                relay.Connector = relayConn;
                relay.Identity = "relay";
                relay.ExchangeName = "out";
                (relay as ISender).Setup = delegate(IModel channel) {
                    DeclareExchange(channel, relay.ExchangeName, "direct");
                };
                (relay as IReceiver).Setup = delegate(IModel channel) {
                    DeclareExchange(channel, "in", "fanout");
                    DeclareQueue(channel, relay.QueueName);
                    BindQueue(channel, relay.QueueName, "in", "");
                };
                relay.Init();

                //run relay
                new System.Threading.Thread
                    (delegate() {
                        //receive messages and pass it on
                        IReceivedMessage r;
                        try {
                            while (true) {
                                r = relay.Receive();
                                relay.Send(r);
                                relay.Ack(r);
                            }
                        } catch (EndOfStreamException) {
                        } catch (AlreadyClosedException) {
                        } catch (OperationInterruptedException) {
                        }
                    }).Start();

                //create two parties
                IMessaging foo = Factory.CreateMessaging();
                foo.Connector = conn;
                foo.Identity = "foo";
                (foo as ISender).Setup = senderSetup(foo);
                (foo as IReceiver).Setup = receiverSetup(foo);
                foo.ExchangeName = "in";
                foo.Sent += TestHelper.Sent;
                foo.Init();
                IMessaging bar = Factory.CreateMessaging();
                bar.Connector = conn;
                bar.Identity = "bar";
                (bar as ISender).Setup = senderSetup(bar);
                (bar as IReceiver).Setup = receiverSetup(bar);
                bar.ExchangeName = "in";
                bar.Sent += TestHelper.Sent;
                bar.Init();

                //send message from foo to bar
                IMessage mf = foo.CreateMessage();
                mf.Body = TestHelper.Encode("message1");
                mf.To   = "bar";
                foo.Send(mf);

                //receive message at bar and reply
                IReceivedMessage rb = bar.Receive();
                TestHelper.LogMessage("recv", bar, rb);
                IMessage mb = bar.CreateReply(rb);
                mb.Body = TestHelper.Encode("message2");
                bar.Send(mb);
                bar.Ack(rb);

                //receive reply at foo
                IReceivedMessage rf = foo.Receive();
                TestHelper.LogMessage("recv", foo, rf);
                foo.Ack(rf);
            }

        }

        protected static void TestPreconfigured(ConnectionFactory factory,
                                                AmqpTcpEndpoint server)  {
            //The idea here is to simulate a setup where are the
            //resources are pre-declared outside the Unicast messaging
            //framework.
            using (IConnection conn = factory.CreateConnection(server)) {
                IModel ch = conn.CreateModel();

                //declare exchanges
                DeclareExchange(ch, "in", "fanout");
                DeclareExchange(ch, "out", "direct");

                //declare queue and binding for relay
                DeclareQueue(ch, "relay");
                BindQueue(ch, "relay", "in", "");

                //declare queue and binding for two participants
                DeclareQueue(ch, "foo");
                BindQueue(ch, "foo", "out", "foo");
                DeclareQueue(ch, "bar");
                BindQueue(ch, "bar", "out", "bar");
            }
            //set up participants, send some messages
            MessagingClosure dummy = delegate(IMessaging m) {
                return delegate(IModel channel) {};
            };
            TestRelayedHelper(dummy, dummy, factory, server);
        }

    }

}
