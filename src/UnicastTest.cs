namespace RabbitMQ.Client.MessagePatterns.Unicast {

    //NB: For testing we declare all resources as
    //auto-delete/exclusive and non-durable, to avoid manual
    //cleanup. More typically the resources would be
    //non-auto-delete/non-exclusive and durable, so that they survives
    //server and client restarts.

    public class Test {

        public static int Main(string[] args) {
            using (IConnection conn = new ConnectionFactory().
                   CreateConnection("localhost")) {
                TestDirect(conn);
                TestRelayed(conn);
                TestPreconfigured(conn);
            }

            return 0;
        }

        protected static void Sent(IMessaging sender, IMessage m) {
                LogMessage("sent", sender, m);
        }

        // NB: For testing we declare all resources as
        // auto-delete/exclusive and non-durable, to avoid manual
        // cleanup. More typically the resources would be declared
        // non-auto-delete/non-exclusive and durable, so that they
        // survives server and client restarts.
        
        protected static void DeclareExchange(IModel m,
                                              string name, string type) {
            m.ExchangeDeclare(name, type,
                              false, false, true, false, false, null);
        }

        protected static void DeclareQueue(IModel m, string name) {
            m.QueueDeclare(name, false, false, true, false, false, null);
        }

        protected static void BindQueue(IModel m,
                                        string q, string x,string rk) {
            m.QueueBind(q, x, rk, false, null);
        }

        protected static void TestDirect(IConnection conn) {
            SetupDelegate setup = delegate(IMessaging m) {
                DeclareQueue(m.ReceivingChannel, m.QueueName);
            };
            using (IMessaging foo = new Messaging(), bar = new Messaging()) {
                //create two parties
                foo.Identity = "foo";
                foo.Sent += Sent;
                foo.Setup = setup;
                foo.Init(conn);
                bar.Identity = "bar";
                bar.Sent += Sent;
                bar.Setup = setup;
                bar.Init(conn);

                //send message from foo to bar
                IMessage mf = new Message();
                mf.Properties = foo.SendingChannel.CreateBasicProperties();
                mf.Body       = Encode("message1");
                mf.From       = foo.Identity;
                mf.To         = "bar";
                mf.MessageId  = foo.NextId();
                foo.Send(mf);

                //receive message at bar and reply
                IReceivedMessage rb = bar.Receive();
                LogMessage("recv", bar, rb);
                IMessage mb = rb.CreateReply();
                mb.Body      = Encode("message2");
                mb.MessageId = bar.NextId();
                bar.Send(mb);
                bar.Ack(rb);

                //receive reply at foo
                IReceivedMessage rf = foo.Receive();
                LogMessage("recv", foo, rf);
                foo.Ack(rf);
            }
}

        protected static void TestRelayed(IConnection conn) {
            TestRelayedHelper
                (conn, delegate(IMessaging m) {
                    DeclareExchange(m.SendingChannel, m.ExchangeName, "fanout");
                    DeclareExchange(m.ReceivingChannel, "out", "direct");
                    DeclareQueue(m.ReceivingChannel, m.QueueName);
                    BindQueue(m.ReceivingChannel,
                              m.QueueName, "out", m.QueueName);
                });
        }

        protected static void TestRelayedHelper(IConnection conn,
                                                SetupDelegate d) {
            using (IMessaging
                   relay = new Messaging(),
                   foo = new Messaging(),
                   bar = new Messaging()) {

                //create relay
                relay.Identity = "relay";
                relay.ExchangeName = "out";
                relay.Setup = delegate(IMessaging m) {
                    DeclareExchange(m.SendingChannel, m.ExchangeName, "direct");
                    DeclareExchange(m.ReceivingChannel, "in", "fanout");
                    DeclareQueue(m.ReceivingChannel, m.QueueName);
                    BindQueue(m.ReceivingChannel, m.QueueName, "in", "");
                };
                relay.Init(conn);

                //activate relay
                new System.Threading.Thread
                    (delegate() {
                        //receive messages and pass it on
                        IReceivedMessage r;
                        while (true) {
                            r = relay.Receive();
                            if (r == null) return;
                            relay.Send(r);
                            relay.Ack(r);
                        }
                    }).Start();

                //create two parties
                foo.Identity = "foo";
                foo.Setup = d;
                foo.ExchangeName = "in";
                foo.Sent += Sent;
                foo.Init(conn);
                bar.Identity = "bar";
                bar.Setup = d;
                bar.ExchangeName = "in";
                bar.Sent += Sent;
                bar.Init(conn);

                //send message from foo to bar
                IMessage mf = new Message();
                mf.Properties = foo.SendingChannel.CreateBasicProperties();
                mf.Body       = Encode("message1");
                mf.From       = foo.Identity;
                mf.To         = "bar";
                mf.MessageId  = foo.NextId();
                foo.Send(mf);

                //receive message at bar and reply
                IReceivedMessage rb = bar.Receive();
                LogMessage("recv", bar, rb);
                IMessage mb = rb.CreateReply();
                mb.Body      = Encode("message2");
                mb.MessageId = bar.NextId();
                bar.Send(mb);
                bar.Ack(rb);

                //receive reply at foo
                IReceivedMessage rf = foo.Receive();
                LogMessage("recv", foo, rf);
                foo.Ack(rf);
            }

        }

        protected static void TestPreconfigured(IConnection conn)  {
            using (IModel ch = conn.CreateModel()) {

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

                //set up participants, send some messages
                TestRelayedHelper(conn, Messaging.DefaultSetup);
            }
        }

        protected static void LogMessage(string action,
                                         IMessaging actor,
                                         IMessage m) {
            System.Console.WriteLine("{0} {1} {2}",
                                     actor.Identity, action, Decode(m.Body));
        }

        protected static byte[] Encode(string s) {
            return System.Text.Encoding.UTF8.GetBytes(s);
        }

        protected static string Decode(byte[] b) {
            return System.Text.Encoding.UTF8.GetString(b);
        }

    }

}
