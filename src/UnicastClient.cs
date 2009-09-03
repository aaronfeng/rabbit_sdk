using System;

namespace RabbitMQ.Client.MessagePatterns.Unicast.Test {

    using Address   = String;
    using MessageId = String;

    using Hashtable = System.Collections.Hashtable;

    using System.Text;

    public class Client {

        public static int Main(string[] args) {
            Client c = new Client();
            c.Run(args[0], args[1], new AmqpTcpEndpoint(),
                  Int32.Parse(args[2]));
            return 0;
        }

        Hashtable peerStats = new Hashtable();

        int sent; //requests sent
        int pend; //pending requests
        int recv; //requests received
        int repl; //replies sent
        int disc; //replies discared
        int dups; //detected received duplicates
        int lost; //detected lost messages

        Client() {
        }

        void Sent(IMessaging sender, IMessage msg) {
            if (msg.CorrelationId == null) {
                sent++;
                pend++;
            } else {
                repl++;
            }
            DisplayStats();
        }

        void UpdatePeerStats(String from, bool redelivered, int seq) {
            if (redelivered) return;

            if (!peerStats.ContainsKey(from)) {
                peerStats[from] = seq;
                return;
            }

            int current = (int)peerStats[from];
            if (seq <= current) {
                dups++;
            } else {
                if (seq > current + 1) {
                    lost += seq - current + 1;
                }
                peerStats[from] = seq;
            }
        }

        void DisplayStats() {
            System.Console.Write("\r" +
                                 "sent {0,6}, " +
                                 "pend {1,6}, " +
                                 "recv {2,6}, " +
                                 "repl {3,6}, " +
                                 "disc {4,6}, " +
                                 "dups {5,6}, " +
                                 "lost {6,6}",
                                 sent, pend, recv, repl, disc, dups, lost);
        }

        void Run(Address me, Address you, AmqpTcpEndpoint server, int sleep) {
            using (IMessaging m = new Messaging()) {
                m.Identity = me;
                m.Sent += Sent;
                m.Setup = delegate(IMessaging u, IModel send, IModel recv) {
                    recv.QueueDeclare(me, true); //durable
                    //We declare the recipient queue here to avoid
                    //sending messages into the ether. That's an ok
                    //thing to do for testing
                    recv.QueueDeclare(you, true); //durable
                };
                m.Init(new ConnectionFactory(), server);
                MessageId baseId = m.CurrentId;
                for (int i = 0;; i++) {
                    //send
                    IMessage msg = m.CreateMessage();
                    msg.Body = Encoding.UTF8.GetBytes("" + i);
                    msg.To   = you;
                    msg.Properties.SetPersistent(true);
                    m.Send(msg);
                    //handle inbound
                    while (true) {
                        IReceivedMessage r = m.ReceiveNoWait();
                        if (r == null) break;
                        if (r.CorrelationId == null) {
                            recv++;
                            int seq = Int32.Parse
                                (Encoding.UTF8.GetString(r.Body));
                            UpdatePeerStats(r.From, r.Redelivered, seq);
                            DisplayStats();
                            m.Send(m.CreateReply(r));
                        } else {
                            if (String.Compare(r.CorrelationId,  baseId) < 0) {
                                disc++;
                            } else {
                                pend--;
                            }
                            DisplayStats();
                        }
                        m.Ack(r);
                    }
                    System.Threading.Thread.Sleep(sleep);
                }
            }
        }
    }
}
