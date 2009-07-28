namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = System.String;
    using MessageId = System.String;
    using Name      = System.String;

    using RabbitMQ.Client;

    public delegate void InitEventHandler(object sender, object dummy);
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

        event InitEventHandler    Initialised;
        event MessageEventHandler Sent;
        event MessageEventHandler Received;

        Address Identity      { get; set; }
        Name    ExchangeName  { get; set; }
        Name    QueueName     { get; set; }
        ushort  PrefetchLimit { get; set; }

        IConnection Connection       { get; }
        IModel      SendingChannel   { get; }
        IModel      ReceivingChannel { get; }

        void Init(IConnection conn);
        void Init(IConnection conn, long msgIdPrefix);
        MessageId NextId();
        void Send(IMessage m);
    }

    public class Messaging : IMessaging {
        //TODO: implement IDisposable

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected Name    m_queueName     = "";
        protected ushort  m_prefetchLimit = 0;

        protected IConnection m_connection;
        protected IModel      m_sendingChannel;
        protected IModel      m_receivingChannel;

        protected long m_msgIdPrefix;
        protected long m_msgIdSuffix;

        public event InitEventHandler    Initialised;
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
        public Name    QueueName     {
            get { return ("".Equals(m_queueName) ? Identity : m_queueName); }
            set { m_queueName = value; }
        }
        public ushort  PrefetchLimit {
            get { return m_prefetchLimit; }
            set { m_prefetchLimit = value; }
        }

        public IConnection Connection       {
            get { return m_connection; }
        }
        public IModel      SendingChannel   {
            get { return m_sendingChannel; }
        }
        public IModel      ReceivingChannel {
            get { return m_receivingChannel; }
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
            SetupRouting();
            CreateSubscription();
        }

        protected void CreateAndConfigureChannels() {
            m_sendingChannel   = m_connection.CreateModel();
            m_receivingChannel = m_connection.CreateModel();
            m_receivingChannel.BasicQos(0, PrefetchLimit, false);
        }

        protected void SetupRouting() {
            if (Initialised != null) {
                Initialised(this, null);
            } else {
                ReceivingChannel.QueueDeclare(QueueName, true);
                if (!"".Equals(ExchangeName)) {
                    ReceivingChannel.QueueBind(QueueName, ExchangeName,
                                               QueueName, false, null);
                }
            }
        }

        protected void CreateSubscription() {
            //TODO: implement
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

}
