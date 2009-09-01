namespace RabbitMQ.Client.MessagePatterns.Unicast.Test {

    using Address   = System.String;
    using MessageId = System.String;

    public class Client {

        public static int Main(string[] args) {
            Client c = new Client();
            c.Run(args[0], args[1], new AmqpTcpEndpoint(),
                  System.Int32.Parse(args[2]));
            return 0;
        }

        int sent; //requests sent
        int pend; //pending requests
        int recv; //requests received
        int repl; //replies sent
        int disc; //replies discared

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

        void DisplayStats() {
            System.Console.Write("\r" +
                                 "sent: {0,8}, " +
                                 "pend: {1,8}, " +
                                 "recv: {2,8}, " +
                                 "repl: {3,8}, " +
                                 "disc: {4,8}",
                                 sent, pend, recv, repl, disc);
        }

        void Run(Address me, Address you, AmqpTcpEndpoint server, int sleep) {
            using (IMessaging m = new Messaging()) {
                m.Identity = me;
                m.Sent += Sent;
                m.Setup = delegate(IMessaging u, IModel send, IModel recv) {
                    recv.QueueDeclare(u.Identity, true); //durable
                };
                m.Init(new ConnectionFactory(), server);
                byte[] body = new byte[0];
                MessageId baseId = m.CurrentId;
                for (int i = 0;; i++) {
                    //send
                    IMessage msg = m.CreateMessage();
                    msg.Body = body;
                    msg.To   = you;
                    msg.Properties.SetPersistent(true);
                    m.Send(msg);
                    //handle inbound
                    while (true) {
                        IReceivedMessage r = m.ReceiveNoWait();
                        if (r == null) break;
                        if (r.CorrelationId == null) {
                            recv++;
                            DisplayStats();
                            m.Send(m.CreateReply(r));
                        } else {
                            if (System.String.Compare
                                (r.CorrelationId,  baseId) < 0) {
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
