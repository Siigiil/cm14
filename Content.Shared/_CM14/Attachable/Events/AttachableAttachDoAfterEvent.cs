using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._CM14.Attachable;

[Serializable, NetSerializable]
public sealed partial class AttachableAttachDoAfterEvent : SimpleDoAfterEvent
{
    public readonly string SlotID;
    
    public AttachableAttachDoAfterEvent(string slotID)
    {
        SlotID = slotID;
    }
}