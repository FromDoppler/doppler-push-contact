namespace Doppler.PushContact.Models.Enums
{
    public enum WebPushEventType
    {
        Delivered = 0,
        Received = 1,
        Clicked = 2,
        ProcessingFailed = 3,
        DeliveryFailed = 4,
        DeliveryFailedButRetry = 5,
        ActionClick = 6,
    }

    public enum WebPushEventSubType
    {
        None = 0,
        InvalidSubcription = 1,
        UnknownFailure = 2,
        LimitsExceeded = 3,
    }
}
