namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using RabbitMQ.Client;
    using Subscription = RabbitMQ.Client.MessagePatterns.Subscription;

    public delegate void MessageEventHandler(object sender, IMessage m);

    public interface IMessage {
        IBasicProperties Properties { get; set; }
        byte[]           Body       { get; set; }
        string           RoutingKey { get; set; }

        Address From            { get; set; }
        Address To              { get; set; }
        Address ReplyTo         { get; set; }
        MessageId MessageId     { get; set; }
        MessageId CorrelationId { get; set; }

        IMessage CreateReply();
    }

    public interface IMessaging {

        event MessageEventHandler Sent;
        event MessageEventHandler Received;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        string  ExchangeType  { get; set; }
        Name    QueueName     { get; set; }
        ushort  PrefetchLimit { get; set; }

        IConnection  Connection       { get; }
        IModel       SendingChannel   { get; }
        IModel       ReceivingChannel { get; }
        Subscription Subscription     { get; }

        void Init(IConnection conn);
        void Init(IConnection conn, long msgIdPrefix);
        MessageId NextId();
        void Send(IMessage m);
    }

    public class Message : IMessage {

        protected IBasicProperties m_properties;
        protected byte[]           m_body;
        protected string           m_routingKey;

        public IBasicProperties Properties {
            get { return m_properties; }
            set { m_properties = value; }
        }
        public byte[]           Body       {
            get { return m_body; }
            set { m_body = value; }
        }
        public string           RoutingKey {
            get { return m_routingKey; }
            set { m_routingKey = value; }
        }

        public Address From            {
            get { return Properties.UserId; }
            set { Properties.UserId = value; }
        }
        public Address To              {
            get { return RoutingKey; }
            set { RoutingKey = value; }
        }
        public Address ReplyTo         {
            get { return Properties.ReplyTo; }
            set { Properties.ReplyTo = value; }
        }
        public MessageId MessageId     {
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
            //IBasicProperties and ICloneable - see bug 21271
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

    public class Messaging : IMessaging {
        //TODO: implement IDisposable

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected string  m_exchangeType  = "direct";
        protected Name    m_queueName     = "";
        protected ushort  m_prefetchLimit = 0;

        protected IConnection  m_connection;
        protected IModel       m_sendingChannel;
        protected IModel       m_receivingChannel;
        protected Subscription m_subscription;

        protected long m_msgIdPrefix;
        protected long m_msgIdSuffix;

        public event MessageEventHandler Sent;
        public event MessageEventHandler Received;

        public Address Identity      {
            get { return m_identity; }
            set { m_identity = value; }
        }
        public Name    ExchangeName  {
            get { return m_exchangeName; }
            set { m_exchangeName = value; }
        }
        public string  ExchangeType  {
            get { return m_exchangeType; }
            set { m_exchangeType = value; }
        }
        public Name    QueueName     {
            get { return ("".Equals(m_queueName) ? Identity : m_queueName); }
            set { m_queueName = value; }
        }
        public ushort  PrefetchLimit {
            get { return m_prefetchLimit; }
            set { m_prefetchLimit = value; }
        }

        public IConnection  Connection       {
            get { return m_connection; }
        }
        public IModel       SendingChannel   {
            get { return m_sendingChannel; }
        }
        public IModel       ReceivingChannel {
            get { return m_receivingChannel; }
        }

        public Subscription Subscription     {
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
            m_subscription = CreateSubscription();
        }

        protected void CreateAndConfigureChannels() {
            m_sendingChannel   = m_connection.CreateModel();
            m_receivingChannel = m_connection.CreateModel();
            m_receivingChannel.BasicQos(0, PrefetchLimit, false);
        }

        protected Subscription CreateSubscription() {
            return ("".Equals(ExchangeName) ?
                    new Subscription(ReceivingChannel, QueueName, false) :
                    new Subscription(ReceivingChannel, QueueName, false,
                                     ExchangeName, ExchangeType, Identity));
        }

        public MessageId NextId() {
            return System.String.Format("{0:x8}{1:x8}",
                                        m_msgIdPrefix, m_msgIdSuffix++);
        }

        public void Send(IMessage m) {
            SendingChannel.BasicPublish(ExchangeName, m.RoutingKey,
                                        m.Properties, m.Body);
        }

    }

    public class Test {

        public static int Main(string[] args) {
            using (IConnection conn = new ConnectionFactory().
                   CreateConnection("localhost")) {
                //create two parties
                IMessaging foo = new Messaging();
                foo.Identity = "foo";
                foo.Init(conn);
                IMessaging bar = new Messaging();
                bar.Identity = "bar";
                bar.Init(conn);
                //send message from foo to bar
                IMessage m1 = new Message();
                m1.Properties = foo.SendingChannel.CreateBasicProperties();
                m1.Body       = System.Text.Encoding.UTF8.GetBytes("message1");
                m1.From       = foo.Identity;
                m1.To         = "bar";
                m1.MessageId  = foo.NextId();
                foo.Send(m1);
                //receive message at bar
                RabbitMQ.Client.Events.BasicDeliverEventArgs r1 =
                    bar.Subscription.Next();
                System.Console.WriteLine(System.Text.Encoding.UTF8.
                                         GetString(r1.Body));
                bar.Subscription.Ack(r1);
            }

            return 0;
        }

    }

}
