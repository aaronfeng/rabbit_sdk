using System;

namespace RabbitMQ.Client.MessagePatterns.Unicast {

    using Address   = String;
    using MessageId = String;
    using Name      = String;

    using EndOfStreamException   = System.IO.EndOfStreamException;

    using BasicDeliverEventArgs  = RabbitMQ.Client.Events.BasicDeliverEventArgs;
    using ClientExceptions       = RabbitMQ.Client.Exceptions;
    using SharedQueue            = RabbitMQ.Util.SharedQueue;
    //TODO: find a protocol version agnostic way of doing this
    using ProtocolConstants      = RabbitMQ.Client.Framing.v0_8.Constants;

    class Message : IMessage {

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
            IMessage m = new Message(Properties.Clone() as IBasicProperties,
                                     Body,
                                     RoutingKey);
            m.From = To;
            m.To = ReplyTo == null ? From : ReplyTo;
            m.Properties.ClearReplyTo();
            m.CorrelationId = MessageId;
            m.Properties.ClearMessageId();

            return m;
        }

    }

    class ReceivedMessage : Message, IReceivedMessage {

        protected IModel m_channel;
        protected BasicDeliverEventArgs m_delivery;

        public bool Redelivered {
            get { return m_delivery.Redelivered; }
        }

        public IModel Channel {
            get { return m_channel; }
        }

        public BasicDeliverEventArgs Delivery {
            get { return m_delivery; }
        }

        public ReceivedMessage(IModel channel, BasicDeliverEventArgs delivery) :
            base(delivery.BasicProperties,
                 delivery.Body,
                 delivery.RoutingKey) {
            m_channel  = channel;
            m_delivery = delivery;
        }

    }

    public class QueueingMessageConsumer : DefaultBasicConsumer {

        protected SharedQueue m_queue;

        public QueueingMessageConsumer(IModel model) : base (model) {
            m_queue = new SharedQueue();
        }

        public SharedQueue Queue
        {
            get { return m_queue; }
        }

        public override void OnCancel()
        {
            m_queue.Close();
            base.OnCancel();
        }

        public override void HandleBasicDeliver(string consumerTag,
                                                ulong deliveryTag,
                                                bool redelivered,
                                                string exchange,
                                                string routingKey,
                                                IBasicProperties properties,
                                                byte[] body)
        {
            BasicDeliverEventArgs e = new BasicDeliverEventArgs();
            e.ConsumerTag = consumerTag;
            e.DeliveryTag = deliveryTag;
            e.Redelivered = redelivered;
            e.Exchange = exchange;
            e.RoutingKey = routingKey;
            e.BasicProperties = properties;
            e.Body = body;
            m_queue.Enqueue(new ReceivedMessage(Model, e));
        }

    }

    public delegate void Thunk();

    class RecoveryHelper {

        private static bool IsShutdownRecoverable(ShutdownEventArgs s) {
            return (s != null &&
                    ((s.ReplyCode == ProtocolConstants.ConnectionForced) ||
                     (s.ReplyCode == ProtocolConstants.InternalError) ||
                     (s.Cause is EndOfStreamException)));
        }

        public static Exception AttemptOperation(Thunk t) {
            try {
                t();
                return null;
            } catch (ClientExceptions.AlreadyClosedException e) {
                if (IsShutdownRecoverable(e.ShutdownReason)) {
                    return e;
                } else {
                    throw e;
                }
            } catch (ClientExceptions.OperationInterruptedException e) {
                if (IsShutdownRecoverable(e.ShutdownReason)) {
                    return e;
                } else {
                    throw e;
                }
            } catch (ClientExceptions.BrokerUnreachableException e) {
                //TODO: we may want to be more specific here
                return e;
            } catch (System.IO.IOException e) {
                //TODO: we may want to be more specific here
                return e;
            }
        }

    }

    public class Connector : IConnector {

        protected int m_pause    = 1000; //ms
        protected int m_attempts = 60;

        protected ConnectionFactory m_factory;
        protected AmqpTcpEndpoint[] m_servers;

        protected IConnection m_connection;

        public int Pause {
            get { return m_pause; }
            set { m_pause = value; }
        }
        public int Attempts {
            get { return m_attempts; }
            set { m_attempts = value; }
        }

        public ConnectionFactory ConnectionFactory {
            get { return m_factory; }
        }
        public AmqpTcpEndpoint[] Servers {
            get { return m_servers; }
        }

        public Connector(ConnectionFactory factory,
                         params AmqpTcpEndpoint[] servers) {
            m_factory = factory;
            m_servers = servers;
        }

        public IConnection Connect() {
            lock (this) {
                if (m_connection != null && m_connection.IsOpen)
                    return m_connection;
                Exception e = null;
                for (int i = 0; i < Attempts; i++) {
                    e = RecoveryHelper.AttemptOperation(delegate {
                            m_connection =
                            ConnectionFactory.CreateConnection(Servers);
                        });
                    if (e == null) return m_connection;
                    System.Threading.Thread.Sleep(Pause);
                }
                throw(e);
            }
        }

        public void Close() {
            //TODO: find a way to abort a pending reconnect, rather
            //than waiting here
            lock (this) {
                if (m_connection != null) m_connection.Abort();
            }
        }

        void IDisposable.Dispose() {
            Close();
        }

    }

    public class Messaging : IMessaging {

        protected Address m_identity;
        protected Name    m_exchangeName  = "";
        protected Name    m_queueName     = "";

        protected IConnector m_connector;

        protected bool m_transactional = true;

        protected SetupDelegate m_setup =
            new SetupDelegate(DefaultSetup);

        protected IModel      m_sendingChannel;
        protected IModel      m_receivingChannel;

        protected QueueingMessageConsumer m_consumer;
        protected string                  m_consumerTag;

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

        public Name QueueName {
            get { return ("".Equals(m_queueName) ? Identity : m_queueName); }
            set { m_queueName = value; }
        }

        public IConnector Connector {
            get { return m_connector; }
            set { m_connector = value; }
        }

        public bool Transactional {
            get { return m_transactional; }
            set { m_transactional = value; }
        }

        public SetupDelegate Setup {
            get { return m_setup; }
            set { m_setup = value; }
        }

        public MessageId CurrentId {
            get { return String.Format("{0:x8}{1:x8}",
                                       m_msgIdPrefix, m_msgIdSuffix); }
        }

        public Messaging() {}

        public void Init() {
            Init(DateTime.UtcNow.Ticks);
        }

        public void Init(long msgIdPrefix) {
            m_msgIdPrefix = msgIdPrefix;
            m_msgIdSuffix = 0;

            InitialConnect();
        }

        protected void InitialConnect() {
            IConnection conn = Connector.Connect();
            Exception e = RecoveryHelper.AttemptOperation(delegate() {
                    Connect(conn); });
            if (e == null) return;
            if (!Reconnect()) throw e;
        }

        protected void Connect(IConnection conn) {
            m_sendingChannel   = conn.CreateModel();
            m_receivingChannel = conn.CreateModel();
            if (Transactional) m_sendingChannel.TxSelect();
            Setup(this, m_sendingChannel, m_receivingChannel);
            Consume();
        }

        protected bool Reconnect() {
            try {
                while (true) {
                    IConnection conn = Connector.Connect();
                    Exception e = RecoveryHelper.AttemptOperation(delegate {
                            Connect(conn); });
                    if (e == null) return true;
                }
            } catch (Exception) {}
            return false;
        }

        protected void Consume() {
            m_consumer = new QueueingMessageConsumer(m_receivingChannel);
            m_consumerTag = m_receivingChannel.BasicConsume
                (QueueName, false, null, m_consumer);
        }

        protected void Cancel() {
            m_receivingChannel.BasicCancel(m_consumerTag);
        }

        protected MessageId NextId() {
            MessageId res = CurrentId;
            m_msgIdSuffix++;
            return res;
        }

        public static void DefaultSetup(IMessaging m,
                                        IModel send, IModel recv) {
        }

        public IMessage CreateMessage() {
            IMessage m = new Message();
            m.Properties = m_sendingChannel.CreateBasicProperties();
            m.From       = Identity;
            m.MessageId  = NextId();
            return m;
        }

        public IMessage CreateReply(IMessage m) {
            IMessage r  = m.CreateReply();
            r.MessageId = NextId();
            return r;
        }

        public void Send(IMessage m) {
            while(true) {
                Exception e = RecoveryHelper.AttemptOperation(delegate () {
                        m_sendingChannel.BasicPublish(ExchangeName,
                                                      m.RoutingKey,
                                                      m.Properties, m.Body);
                        if (Transactional) m_sendingChannel.TxCommit();
                    });
                if (e == null) break;
                if (!Reconnect()) throw e;
            }
            //TODO: if/when IModel supports 'sent' notifications then
            //we will translate those, rather than firing our own here
            if (Sent != null) Sent(this, m);
        }

        public IReceivedMessage Receive() {
            while(true) {
                try {
                    return m_consumer.Queue.Dequeue()
                        as IReceivedMessage;
                } catch (EndOfStreamException e) {
                    if (!Reconnect()) throw e;
                }
            }
        }

        public IReceivedMessage ReceiveNoWait() {
            while (true) {
                try {
                    return m_consumer.Queue.DequeueNoWait(null)
                        as IReceivedMessage;
                } catch (EndOfStreamException e) {
                    if (!Reconnect()) throw e;
                }
            }
        }

        public void Ack(IReceivedMessage m) {
            ReceivedMessage r = m as ReceivedMessage;
            if (r.Channel != m_receivingChannel) {
                //must have been reconnected; drop ack since there is
                //no place for it to go
                return;
            }
            Exception e = RecoveryHelper.AttemptOperation(delegate () {
                    m_receivingChannel.BasicAck(r.Delivery.DeliveryTag, false);
                });
            if (e == null) return;
            //Acks must not be retried since they are tied to the
            //channel on which the message was delivered
            if (!Reconnect()) throw e;
        }

    }

}
