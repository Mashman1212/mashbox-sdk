
namespace MashBoxBridge.Common.Interfaces
{
    public interface IContentItem : IDataLabel, IDataDescription, IDataIcon, IDataID, IContentTier
    {
        ulong Uid { get; }
    }
}
