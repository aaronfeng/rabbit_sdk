namespace RabbitMQ.Client.MessagePatterns.Unicast.Test {

    using EndOfStreamException   = System.IO.EndOfStreamException;
    using RabbitMQ.Client.Exceptions;

    //TODO: this class should live in a separate assembly
    public class TestHelper {

        public static void Sent(IMessaging sender, IMessage m) {
                LogMessage("sent", sender, m);
        }

        public static void LogMessage(string action,
                                         IMessaging actor,
                                         IMessage m) {
            System.Console.WriteLine("{0} {1} {2}",
                                     actor.Identity, action, Decode(m.Body));
        }

        public static byte[] Encode(string s) {
            return System.Text.Encoding.UTF8.GetBytes(s);
        }

        public static string Decode(byte[] b) {
            return System.Text.Encoding.UTF8.GetString(b);
        }

    }


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
            SetupDelegate setup = delegate(IMessaging m,
                                           IModel send, IModel recv) {
                DeclareQueue(recv, m.QueueName);
            };
            using (IConnector conn = new Connector(factory, server)) {
                IMessaging foo = new Messaging();
                IMessaging bar = new Messaging();
                //create two parties
                foo.Connector = conn;
                foo.Identity = "foo";
                foo.Sent += TestHelper.Sent;
                foo.Setup = setup;
                foo.Init();
                bar.Connector = conn;
                bar.Identity = "bar";
                bar.Sent += TestHelper.Sent;
                bar.Setup = setup;
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
            TestRelayedHelper
                (delegate(IMessaging m, IModel send, IModel recv) {
                    DeclareExchange(send, m.ExchangeName, "fanout");
                    DeclareExchange(recv, "out", "direct");
                    DeclareQueue(recv, m.QueueName);
                    BindQueue(recv, m.QueueName, "out", m.QueueName);
                }, factory, server);
        }

        protected static void TestRelayedHelper(SetupDelegate d,
                                                ConnectionFactory factory,
                                                AmqpTcpEndpoint server) {
            using (IConnector conn = new Connector(factory, server),
                   relayConn = new Connector(factory, server)) {

                //create relay 
                IMessaging relay = new Messaging();
                relay.Connector = relayConn;
                relay.Identity = "relay";
                relay.ExchangeName = "out";
                relay.Setup = delegate(IMessaging m, IModel send,
                                       IModel recv) {
                    DeclareExchange(send, m.ExchangeName, "direct");
                    DeclareExchange(recv, "in", "fanout");
                    DeclareQueue(recv, m.QueueName);
                    BindQueue(recv, m.QueueName, "in", "");
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
                IMessaging foo = new Messaging();
                foo.Connector = conn;
                foo.Identity = "foo";
                foo.Setup = d;
                foo.ExchangeName = "in";
                foo.Sent += TestHelper.Sent;
                foo.Init();
                IMessaging bar = new Messaging();
                bar.Connector = conn;
                bar.Identity = "bar";
                bar.Setup = d;
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
            TestRelayedHelper(Messaging.DefaultSetup, factory, server);
        }

    }

}
