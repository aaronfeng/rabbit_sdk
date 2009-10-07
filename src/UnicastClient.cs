using System;

namespace RabbitMQX.Client.MessagePatterns.Unicast.Test {

    using RabbitMQ.Client;
    using RabbitMQ.Client.MessagePatterns.Unicast;

    using Address   = String;
    using MessageId = String;

    using Hashtable = System.Collections.Hashtable;

    public class Client {

        public static int Main(string[] args) {
            Client c = new Client();
            c.Run(args[0], args[1], new AmqpTcpEndpoint(),
                  Int32.Parse(args[2]));
            return 0;
        }

        Hashtable pending   = new Hashtable();

        int sent; //requests sent
        int recv; //requests received
        int pend; //pending requests
        int disc; //replies discarded

        Client() {
        }

        void Sent(IMessage msg) {
            if (msg.CorrelationId == null) {
                sent++;
                pend++;
            }
            DisplayStats();
        }

        void DisplayStats() {
            System.Console.Write("\r" +
                                 "sent {0,6}, " +
                                 "recv {1,6}, " +
                                 "pend {2,6}, " +
                                 "disc {3,6}",
                                 sent, recv, pend, disc);
        }

        void Run(Address me, Address you, AmqpTcpEndpoint server, int sleep) {
            using (IConnector conn =
                   Factory.CreateConnector(new ConnectionFactory(), server)) {
                IMessaging m = Factory.CreateMessaging();
                m.Connector = conn;
                m.Identity = me;
                m.Sent += Sent;
                (m as ISender).Setup = delegate(IModel channel) {
                    //We declare the recipient queue here to avoid
                    //sending messages into the ether. That's an ok
                    //thing to do for testing
                    channel.QueueDeclare(you, true); //durable
                };
                (m as IReceiver).Setup = delegate(IModel channel) {
                    channel.QueueDeclare(me, true); //durable
                };
                m.Init();
                byte[] body = new byte[0];
                for (int i = 0;; i++) {
                    //send
                    IMessage msg = m.CreateMessage();
                    msg.Body = body;
                    msg.To   = you;
                    msg.Properties.SetPersistent(true);
                    m.Send(msg);
                    pending.Add(msg.MessageId, true);
                    //handle inbound
                    while (true) {
                        IReceivedMessage r = m.ReceiveNoWait();
                        if (r == null) break;
                        if (r.CorrelationId == null) {
                            recv++;
                            DisplayStats();
                            m.Send(m.CreateReply(r));
                        } else {
                            if (pending.ContainsKey(r.CorrelationId)) {
                                pending.Remove(r.CorrelationId);
                                pend--;
                            } else {
                                disc++;
                            }
                            DisplayStats();
                        }
                        m.Ack(r);
                    }
                    //Slow down to prevent pending from growing w/o bounds
                    System.Threading.Thread.Sleep(sleep + pend);
                }
            }
        }
    }
}
