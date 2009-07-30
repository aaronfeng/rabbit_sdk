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

}
