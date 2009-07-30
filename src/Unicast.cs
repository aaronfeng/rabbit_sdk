using System;

namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using RabbitMQ.Client;
    using Subscription = RabbitMQ.Client.MessagePatterns.Subscription;
    using BasicDeliverEventArgs = RabbitMQ.Client.Events.BasicDeliverEventArgs;

    public delegate void MessageEventHandler(IMessaging sender, IMessage m);

    public interface IMessage {
        IBasicProperties Properties { get; set; }
        byte[]           Body       { get; set; }
        string           RoutingKey { get; set; }

        Address   From          { get; set; }
        Address   To            { get; set; }
        Address   ReplyTo       { get; set; }
        MessageId MessageId     { get; set; }
        MessageId CorrelationId { get; set; }

        IMessage CreateReply();
    }

    public interface IReceivedMessage : IMessage {
    }

    public delegate Subscription CreateSubscriptionDelegate(IMessaging m);

    public interface IMessaging : IDisposable {

        event MessageEventHandler Sent;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        string  ExchangeType  { get; set; }
        Name    QueueName     { get; set; }
        ushort  PrefetchLimit { get; set; }

        CreateSubscriptionDelegate CreateSubscription { get; set; }

        IConnection  Connection       { get; }
        IModel       SendingChannel   { get; }
        IModel       ReceivingChannel { get; }
        Subscription Subscription     { get; }

        void Init(IConnection conn);
        void Init(IConnection conn, long msgIdPrefix);

        MessageId        NextId();
        void             Send(IMessage m);
        IReceivedMessage Receive();
        void             Ack(IReceivedMessage m);
    }

    public class Message : IMessage {

        protected IBasicProperties m_properties;
        protected byte[]           m_body;
        protected string           m_routingKey;

        public IBasicProperties Properties {
            get { return m_properties; }
            set { m_properties = value; }
        }
        public byte[] Body {
            get { return m_body; }
            set { m_body = value; }
        }
        public string RoutingKey {
            get { return m_routingKey; }
            set { m_routingKey = value; }
        }

        public Address From {
            get { return Properties.UserId; }
            set { Properties.UserId = value; }
        }
        public Address To {
            get { return RoutingKey; }
            set { RoutingKey = value; }
        }
        public Address ReplyTo {
            get { return Properties.ReplyTo; }
            set { Properties.ReplyTo = value; }
        }
        public MessageId MessageId {
            get { return Properties.MessageId; }
            set { Properties.MessageId = value; }
        }
        public MessageId CorrelationId {
            get { return Properties.CorrelationId; }
            set { Properties.CorrelationId = value; }
        }

        public Message() {
        }

        public Message(IBasicProperties props, byte[] body, string rk) {
            m_properties = props;
            m_body       = body;
            m_routingKey = rk;
        }

        public IMessage CreateReply() {
            //FIXME: this should clone Properties, once we have made
            //IBasicProperties an ICloneable - see bug 21271
            IMessage m = new Message(Properties, Body, RoutingKey);

            Address origFrom = From; //TODO: drop once we clone
            m.From = To;
            m.To = ReplyTo == null ? origFrom : ReplyTo;
            m.Properties.ClearReplyTo();
            m.CorrelationId = MessageId;
            m.Properties.ClearMessageId();

            return m;
        }

    }

    public class ReceivedMessage : Message, IReceivedMessage {

        protected BasicDeliverEventArgs m_delivery;

        public BasicDeliverEventArgs Delivery {
            get { return m_delivery; }
        }

        public ReceivedMessage(BasicDeliverEventArgs delivery) :
            base(delivery.BasicProperties,
                 delivery.Body,
                 delivery.RoutingKey) {
            m_delivery = delivery;
        }

    }

    public class Messaging : IMessaging {
        //TODO: implement IDisposable

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected string  m_exchangeType  = "direct";
        protected Name    m_queueName     = "";
        protected ushort  m_prefetchLimit = 0;

        protected CreateSubscriptionDelegate m_createSubscription =
            new CreateSubscriptionDelegate(DefaultCreateSubscription);

        protected IConnection  m_connection;
        protected IModel       m_sendingChannel;
        protected IModel       m_receivingChannel;
        protected Subscription m_subscription;

        protected long m_msgIdPrefix;
        protected long m_msgIdSuffix;

        public event MessageEventHandler Sent;

        public Address Identity {
            get { return m_identity; }
            set { m_identity = value; }
        }
        public Name ExchangeName {
            get { return m_exchangeName; }
            set { m_exchangeName = value; }
        }
        public string ExchangeType {
            get { return m_exchangeType; }
            set { m_exchangeType = value; }
        }
        public Name QueueName {
            get { return ("".Equals(m_queueName) ? Identity : m_queueName); }
            set { m_queueName = value; }
        }
        public ushort PrefetchLimit {
            get { return m_prefetchLimit; }
            set { m_prefetchLimit = value; }
        }

        public CreateSubscriptionDelegate CreateSubscription {
            get { return m_createSubscription; }
            set { m_createSubscription = value; }
        }

        public IConnection Connection {
            get { return m_connection; }
        }
        public IModel SendingChannel {
            get { return m_sendingChannel; }
        }
        public IModel ReceivingChannel {
            get { return m_receivingChannel; }
        }

        public Subscription Subscription {
            get { return m_subscription; }
        }

        public Messaging() {}

        public void Init(IConnection conn) {
            Init(conn, System.DateTime.UtcNow.Ticks);
        }

        public void Init(IConnection conn, long msgIdPrefix) {
            m_connection = conn;
            m_msgIdPrefix = msgIdPrefix;
            m_msgIdSuffix = 0;

            CreateAndConfigureChannels();
            m_subscription = CreateSubscription(this);
        }

        protected void CreateAndConfigureChannels() {
            m_sendingChannel   = m_connection.CreateModel();
            m_receivingChannel = m_connection.CreateModel();
            m_receivingChannel.BasicQos(0, PrefetchLimit, false);
        }

        protected static Subscription DefaultCreateSubscription(IMessaging m) {
            return ("".Equals(m.ExchangeName) ?
                    new Subscription(m.ReceivingChannel, m.QueueName, false) :
                    new Subscription(m.ReceivingChannel, m.QueueName, false,
                                     m.ExchangeName, m.ExchangeType,
                                     m.Identity));
        }

        public MessageId NextId() {
            return System.String.Format("{0:x8}{1:x8}",
                                        m_msgIdPrefix, m_msgIdSuffix++);
        }

        public void Send(IMessage m) {
            SendingChannel.BasicPublish(ExchangeName, m.RoutingKey,
                                        m.Properties, m.Body);
            //TODO: if/when SendingChannel supports 'sent'
            //notifications then we will translate those, rather than
            //firing our own here
            if (Sent != null) Sent(this, m);
        }

        public IReceivedMessage Receive() {
            BasicDeliverEventArgs e = Subscription.Next();
            return (e == null) ? null : new ReceivedMessage(e);
        }

        public void Ack(IReceivedMessage m) {
            Subscription.Ack(((ReceivedMessage)m).Delivery);
        }

        public void Close() {
            //FIXME: only do this if we are fully initialised
            Subscription.Close();
            SendingChannel.Abort();
            ReceivingChannel.Abort();
        }

        void IDisposable.Dispose() {
            Close();
        }

    }

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
