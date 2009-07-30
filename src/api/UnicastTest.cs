using System;

namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using RabbitMQ.Client;
    using Subscription = RabbitMQ.Client.MessagePatterns.Subscription;
    using BasicDeliverEventArgs = RabbitMQ.Client.Events.BasicDeliverEventArgs;

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

        protected static void TestDirect(IConnection conn) {
            using (IMessaging foo = new Messaging(), bar = new Messaging()) {
                //create two parties
                foo.Identity = "foo";
                foo.Sent += Sent;
                foo.Init(conn);
                bar.Identity = "bar";
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

        protected static void TestRelayed(IConnection conn) {
            try {
                TestRelayedHelper
                    (conn, delegate(IMessaging m) {
                        return new Subscription(m.ReceivingChannel,
                                                m.QueueName, false,
                                                "out", "direct", m.Identity);
                    });
            }
            //cleanup resources left over by test
            finally {
                using (IModel ch = conn.CreateModel()) {
                    Cleanup(ch);
                }
            }
        }

        protected static void TestRelayedHelper(IConnection conn,
                                                CreateSubscriptionDelegate d) {
            using (IMessaging
                   relay = new Messaging(),
                   foo = new Messaging(),
                   bar = new Messaging()) {

                //create relay
                relay.Identity = "relay";
                relay.ExchangeName = "out";
                relay.ExchangeType = "direct";
                relay.CreateSubscription = delegate(IMessaging m) {
                    return new Subscription(m.ReceivingChannel,
                                            m.QueueName, false,
                                            "in", "fanout", "");
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
                foo.CreateSubscription = d;
                foo.ExchangeName = "in";
                foo.ExchangeType = "fanout";
                foo.Sent += Sent;
                foo.Init(conn);
                bar.Identity = "bar";
                bar.CreateSubscription = d;
                bar.ExchangeName = "in";
                bar.ExchangeType = "fanout";
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
                ch.ExchangeDeclare("in", "fanout", true);
                ch.ExchangeDeclare("out", "direct", true);

                //declare queue and binding for relay
                ch.QueueDeclare("relay", true);
                ch.QueueBind("relay", "in", "", false, null);

                //declare queue and binding for two participants
                ch.QueueDeclare("foo", true);
                ch.QueueBind("foo", "out", "foo", false, null);
                ch.QueueDeclare("bar", true);
                ch.QueueBind("bar", "out", "bar", false, null);

                //set up participants, send some messages, cleanup
                try {
                    TestRelayedHelper
                        (conn, delegate(IMessaging m) {
                            return new PassiveSubscription(m.ReceivingChannel,
                                                           m.QueueName);
                        });
                }
                //cleanup previously declared resources
                finally {
                    Cleanup(ch);
                }
            }
        }

        protected static void Cleanup(IModel ch) {
            foreach (string q in new string[] {"relay", "foo", "bar"}) {
                ch.QueueDelete(q, false, false, false);
            }
            foreach (string e in new string[] {"in", "out"}) {
                ch.ExchangeDelete(e, false, false);
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

    //This doesn't actually work yet since Subscription at present
    //does not allow us to override how/whether queues are
    //declared. That will be addressed in bug 21286
    public class PassiveSubscription : Subscription {

        public PassiveSubscription(IModel model, string queueName) :
            base(model, queueName, false) {
        }

        public bool DeclareQueue(IModel model, string queueName) {
            return false;
        }

    }

}
