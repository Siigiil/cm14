using Content.Shared.Anomaly;
using Content.Shared._CM14.Attachable;
using Robust.Client.GameObjects;


namespace Content.Client._CM14.Attachable.Ui;

public sealed class AttachableHolderChooseSlotBoundUserInterface : BoundUserInterface
{
    private AttachableHolderChooseSlotMenu? _menu;
    
    
    public AttachableHolderChooseSlotBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }
    
    
    protected override void Open()
    {
        base.Open();
        
        _menu = new AttachableHolderChooseSlotMenu(this);
        
        EntityQuery<MetaDataComponent> metaQuery = EntMan.GetEntityQuery<MetaDataComponent>();
        if(metaQuery.TryGetComponent(Owner, out MetaDataComponent? metadata) && metadata != null)
            _menu.Title = metadata.EntityName;
        _menu.OpenCentered();
    }
    
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not AttachableHolderChooseSlotUserInterfaceState msg)
            return;

        if (_menu == null)
            return;

        _menu.UpdateMenu(msg.AttachableSlots);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _menu?.Dispose();
    }
}
