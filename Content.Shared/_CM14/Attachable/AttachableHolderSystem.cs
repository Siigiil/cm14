using System.Diagnostics.CodeAnalysis;
using Content.Shared._CM14.Attachable.Components;
using Content.Shared._CM14.Attachable.Events;
using Content.Shared._CM14.Input;
using Content.Shared._CM14.Weapons.Common;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory.Events;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;

namespace Content.Shared._CM14.Attachable;

public sealed class AttachableHolderSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly AttachableToggleableSystem _attachableToggleable = default!;
    [Dependency] private readonly AttachableWeaponRangedModsSystem _attachableWeaponRangedMods = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AttachableHolderComponent, AttachableAttachDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<AttachableHolderComponent, AttachableDetachDoAfterEvent>(OnDetachDoAfter);
        SubscribeLocalEvent<AttachableHolderComponent, AttachableHolderAttachablesAlteredEvent>(
            OnAttachableHolderAttachablesAltered);
        SubscribeLocalEvent<AttachableHolderComponent, AttachableHolderAttachToSlotMessage>(
            OnAttachableHolderAttachToSlotMessage);
        SubscribeLocalEvent<AttachableHolderComponent, AttachableHolderDetachMessage>(OnAttachableHolderDetachMessage);
        SubscribeLocalEvent<AttachableHolderComponent, AttemptShootEvent>(OnAttachableHolderAttemptShoot);
        SubscribeLocalEvent<AttachableHolderComponent, BoundUIOpenedEvent>(OnAttachableHolderUiOpened);
        SubscribeLocalEvent<AttachableHolderComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<AttachableHolderComponent, GetVerbsEvent<InteractionVerb>>(OnAttachableHolderGetVerbs);
        SubscribeLocalEvent<AttachableHolderComponent, GotUnequippedEvent>(OnHolderGotUnequipped);
        SubscribeLocalEvent<AttachableHolderComponent, GotUnequippedHandEvent>(OnHolderGotUnequippedHand);
        SubscribeLocalEvent<AttachableHolderComponent, GunRefreshModifiersEvent>(OnAttachableHolderRefreshModifiers,
            after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<AttachableHolderComponent, InteractUsingEvent>(OnAttachableHolderInteractUsing);
        SubscribeLocalEvent<AttachableHolderComponent, ItemWieldedEvent>(OnHolderWielded);
        SubscribeLocalEvent<AttachableHolderComponent, ItemUnwieldedEvent>(OnHolderUnwielded);
        SubscribeLocalEvent<AttachableHolderComponent, UniqueActionEvent>(OnAttachableHolderUniqueAction);

        CommandBinds.Builder
            .Bind(CMKeyFunctions.CMActivateAttachableBarrel,
                InputCmdHandler.FromDelegate(session =>
                    {
                        if (session?.AttachedEntity is { } userUid)
                            ToggleAttachable(userUid, "cm-aslot-barrel");
                    },
                    handle: false))
            .Bind(CMKeyFunctions.CMActivateAttachableRail,
                InputCmdHandler.FromDelegate(session =>
                    {
                        if (session?.AttachedEntity is { } userUid)
                            ToggleAttachable(userUid, "cm-aslot-rail");
                    },
                    handle: false))
            .Bind(CMKeyFunctions.CMActivateAttachableStock,
                InputCmdHandler.FromDelegate(session =>
                    {
                        if (session?.AttachedEntity is { } userUid)
                            ToggleAttachable(userUid, "cm-aslot-stock");
                    },
                    handle: false))
            .Bind(CMKeyFunctions.CMActivateAttachableUnderbarrel,
                InputCmdHandler.FromDelegate(session =>
                    {
                        if (session?.AttachedEntity is { } userUid)
                            ToggleAttachable(userUid, "cm-aslot-underbarrel");
                    },
                    handle: false))
            .Register<AttachableHolderSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<AttachableHolderSystem>();
    }

    private void OnGetActions(Entity<AttachableHolderComponent> holder, ref GetItemActionsEvent args)
    {
        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            if (!_container.TryGetContainer(holder, slotId, out var container) || container.Count <= 0)
                continue;

            if (!TryComp<AttachableToggleableComponent>(container.ContainedEntities[0],
                    out var toggleableComponent))
                continue;

            _attachableToggleable.GrantAction((container.ContainedEntities[0], toggleableComponent), args.User);
        }
    }

    private void OnAttachableHolderInteractUsing(Entity<AttachableHolderComponent> holder, ref InteractUsingEvent args)
    {
        if (CanAttach(holder, args.Used))
        {
            StartAttach(holder, args.Used, args.User);
            args.Handled = true;
        }

        if (holder.Comp.SupercedingAttachable == null)
            return;

        var interactUsingEvent = new InteractUsingEvent(args.User,
            args.Used,
            holder.Comp.SupercedingAttachable.Value,
            args.ClickLocation);
        RaiseLocalEvent(holder.Comp.SupercedingAttachable.Value, interactUsingEvent);

        if (interactUsingEvent.Handled)
        {
            args.Handled = true;
            return;
        }

        var afterInteractEvent = new AfterInteractEvent(args.User,
            args.Used,
            holder.Comp.SupercedingAttachable.Value,
            args.ClickLocation,
            true);
        RaiseLocalEvent(args.Used, afterInteractEvent);

        if (afterInteractEvent.Handled)
            args.Handled = true;
    }

    private void OnAttachableHolderAttemptShoot(Entity<AttachableHolderComponent> holder, ref AttemptShootEvent args)
    {
        if (holder.Comp.SupercedingAttachable == null)
            return;

        args.Cancelled = true;

        if (!TryComp<GunComponent>(holder.Owner, out var holderGunComponent) ||
            holderGunComponent.ShootCoordinates == null ||
            !TryComp<GunComponent>(holder.Comp.SupercedingAttachable,
                out var attachableGunComponent))
        {
            return;
        }

        _gun.AttemptShoot(args.User,
            holder.Comp.SupercedingAttachable.Value,
            attachableGunComponent,
            holderGunComponent.ShootCoordinates.Value);
    }

    private void OnAttachableHolderUniqueAction(Entity<AttachableHolderComponent> holder, ref UniqueActionEvent args)
    {
        if (holder.Comp.SupercedingAttachable == null || args.Handled)
            return;

        RaiseLocalEvent(holder.Comp.SupercedingAttachable.Value, new UniqueActionEvent(args.UserUid));
        args.Handled = true;
    }

    private void OnAttachableHolderAttachablesAltered(Entity<AttachableHolderComponent> holder,
        ref AttachableHolderAttachablesAlteredEvent args)
    {
        if (TryComp<GunComponent>(holder.Owner, out var gunComponent))
            _gun.RefreshModifiers((holder.Owner, gunComponent));
    }

    private void OnAttachableHolderRefreshModifiers(Entity<AttachableHolderComponent> holder,
        ref GunRefreshModifiersEvent args)
    {
        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            if (!_container.TryGetContainer(holder, slotId, out var container) || container.Count <= 0)
                continue;

            var attachableUid = container.ContainedEntities[0];

            if (!TryComp<AttachableComponent>(attachableUid, out _))
                continue;

            _attachableWeaponRangedMods.ApplyWeaponModifiers(attachableUid, ref args);
        }
    }

    private void OnHolderWielded(Entity<AttachableHolderComponent> holder, ref ItemWieldedEvent args)
    {
        ApplyModifiers(holder, AttachableAlteredType.Wielded);
    }

    private void OnHolderUnwielded(Entity<AttachableHolderComponent> holder, ref ItemUnwieldedEvent args)
    {
        ApplyModifiers(holder, AttachableAlteredType.Unwielded);
    }

    private void OnAttachableHolderDetachMessage(EntityUid holderUid,
        AttachableHolderComponent holderComponent,
        AttachableHolderDetachMessage args)
    {
        StartDetach((holderUid, holderComponent), args.Slot, args.Actor);
    }

    private void OnAttachableHolderGetVerbs(Entity<AttachableHolderComponent> holder,
        ref GetVerbsEvent<InteractionVerb> args)
    {
        EnsureSlots(holder);
    }

    private void OnAttachableHolderAttachToSlotMessage(EntityUid holderUid,
        AttachableHolderComponent holderComponent,
        AttachableHolderAttachToSlotMessage args)
    {
        TryComp<HandsComponent>(args.Actor, out var handsComponent);

        if (handsComponent == null)
            return;

        _hands.TryGetActiveItem((args.Actor, handsComponent), out var attachableUid);

        if (attachableUid == null)
            return;

        StartAttach((holderUid, holderComponent), attachableUid.Value, args.Actor, args.Slot);
    }

    private void OnAttachableHolderUiOpened(EntityUid holderUid,
        AttachableHolderComponent holderComponent,
        BoundUIOpenedEvent args)
    {
        UpdateStripUi(holderUid);
    }

    public void StartAttach(Entity<AttachableHolderComponent> holder,
        EntityUid attachableUid,
        EntityUid userUid,
        string slotId = "")
    {
        var validSlots = GetValidSlots(holder, attachableUid);

        if (validSlots.Count == 0)
            return;

        if (string.IsNullOrEmpty(slotId))
        {
            if (validSlots.Count > 1)
            {
                TryComp<UserInterfaceComponent>(holder.Owner,
                    out var userInterfaceComponent);
                _ui.OpenUi((holder.Owner, userInterfaceComponent), AttachmentUI.ChooseSlotKey, userUid);

                var state =
                    new AttachableHolderChooseSlotUserInterfaceState(validSlots);
                _ui.SetUiState(holder.Owner, AttachmentUI.ChooseSlotKey, state);
                return;
            }

            slotId = validSlots[0];
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            userUid,
            Comp<AttachableComponent>(attachableUid).AttachDoAfter,
            new AttachableAttachDoAfterEvent(slotId),
            holder,
            target: holder.Owner,
            used: attachableUid)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnAttachDoAfter(EntityUid uid, AttachableHolderComponent component, AttachableAttachDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (args.Target is not { } target || args.Used is not { } used)
            return;

        if (!TryComp(args.Target, out AttachableHolderComponent? holder) ||
            !HasComp<AttachableComponent>(args.Used))
            return;

        if (Attach((target, holder), used, args.User, args.SlotId))
            args.Handled = true;
    }

    public bool Attach(Entity<AttachableHolderComponent> holder,
        EntityUid attachableUid,
        EntityUid userUid,
        string slotId = "")
    {
        if (!CanAttach(holder, attachableUid, ref slotId))
            return false;

        var container = _container.EnsureContainer<ContainerSlot>(holder, slotId);
        container.OccludesLight = false;

        if (container.Count > 0 && !Detach(holder, attachableUid, userUid, slotId))
            return false;

        if (!_container.Insert(attachableUid, container))
            return false;

        UpdateStripUi(holder.Owner, holder.Comp);

        var holderEv = new AttachableHolderAttachablesAlteredEvent(attachableUid, slotId, AttachableAlteredType.Attached);
        RaiseLocalEvent(holder, ref holderEv);

        var ev = new AttachableAlteredEvent(holder.Owner, AttachableAlteredType.Attached);
        RaiseLocalEvent(attachableUid, ref ev);

        _audio.PlayPredicted(Comp<AttachableComponent>(attachableUid).AttachSound,
            holder,
            userUid);

        Dirty(holder);

        return true;
    }

    //Detaching
    public void StartDetach(Entity<AttachableHolderComponent> holder, string slotId, EntityUid userUid)
    {
        if (TryGetAttachable(holder, slotId, out var attachable))
            StartDetach(holder, attachable.Owner, userUid);
    }

    public void StartDetach(Entity<AttachableHolderComponent> holder, EntityUid attachableUid, EntityUid userUid)
    {
        var delay = Comp<AttachableComponent>(attachableUid).AttachDoAfter;
        var args = new DoAfterArgs(
            EntityManager,
            userUid,
            delay,
            new AttachableDetachDoAfterEvent(),
            holder,
            holder.Owner,
            attachableUid)
        {
            NeedHand = true,
            BreakOnMove = true,
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnDetachDoAfter(EntityUid uid, AttachableHolderComponent component, AttachableDetachDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null || args.Used == null)
            return;

        if (!HasComp<AttachableHolderComponent>(args.Target) || !HasComp<AttachableComponent>(args.Used))
            return;

        if (!Detach((args.Target.Value,
                    Comp<AttachableHolderComponent>(args.Target.Value)),
                args.Used.Value,
                args.User))
            return;

        args.Handled = true;
    }

    public bool Detach(Entity<AttachableHolderComponent> holder,
        EntityUid attachableUid,
        EntityUid userUid,
        string slotId = "")
    {
        if (TerminatingOrDeleted(holder) || !holder.Comp.Running)
            return false;

        if (string.IsNullOrEmpty(slotId))
            CanAttach(holder, attachableUid, ref slotId);

        if (!_container.TryGetContainer(holder, slotId, out var container) || container.Count <= 0)
            return false;

        if (!TryGetAttachable(holder, slotId, out var attachable))
            return false;

        _container.TryRemoveFromContainer(attachable);
        {
            UpdateStripUi(holder.Owner, holder.Comp);

            var holderEv = new AttachableHolderAttachablesAlteredEvent(attachableUid, slotId, AttachableAlteredType.Detached);
            RaiseLocalEvent(holder.Owner, ref holderEv);

            var ev = new AttachableAlteredEvent(holder.Owner, AttachableAlteredType.Detached, userUid);
            RaiseLocalEvent(attachableUid, ref ev);

            _audio.PlayPredicted(Comp<AttachableComponent>(attachableUid).DetachSound,
                holder,
                userUid);

            Dirty(holder);
        }
        _hands.TryPickupAnyHand(userUid, attachable);
        return true;
    }

    private bool CanAttach(Entity<AttachableHolderComponent> holder, EntityUid attachableUid)
    {
        var slotId = "";
        return CanAttach(holder, attachableUid, ref slotId);
    }

    private bool CanAttach(Entity<AttachableHolderComponent> holder, EntityUid attachableUid, ref string slotId)
    {
        if (!HasComp<AttachableComponent>(attachableUid))
            return false;

        if (!string.IsNullOrWhiteSpace(slotId))
            return _whitelist.IsWhitelistPass(holder.Comp.Slots[slotId], attachableUid);

        foreach (var key in holder.Comp.Slots.Keys)
        {
            if (_whitelist.IsWhitelistPass(holder.Comp.Slots[key], attachableUid))
            {
                slotId = key;
                return true;
            }
        }

        return false;
    }

    private Dictionary<string, string?> GetSlotsForStripUi(Entity<AttachableHolderComponent> holder)
    {
        var result = new Dictionary<string, string?>();
        var metaQuery = GetEntityQuery<MetaDataComponent>();

        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            if (TryGetAttachable(holder, slotId, out var attachable) &&
                metaQuery.TryGetComponent(attachable.Owner, out var metadata))
            {
                result.Add(slotId, metadata.EntityName);
            }
            else
            {
                result.Add(slotId, null);
            }
        }

        return result;
    }

    public bool TryGetAttachable(Entity<AttachableHolderComponent> holder,
        string slotId,
        out Entity<AttachableComponent> attachable)
    {
        attachable = default;

        if (!_container.TryGetContainer(holder, slotId, out var container) || container.Count <= 0)
            return false;

        var ent = container.ContainedEntities[0];
        if (!TryComp(ent, out AttachableComponent? attachableComp))
            return false;

        attachable = (ent, attachableComp);
        return true;
    }

    private void UpdateStripUi(EntityUid holderUid, AttachableHolderComponent? holderComponent = null)
    {
        if (!Resolve(holderUid, ref holderComponent))
            return;

        var state =
            new AttachableHolderStripUserInterfaceState(GetSlotsForStripUi((holderUid, holderComponent)));
        _ui.SetUiState(holderUid, AttachmentUI.StripKey, state);
    }

    private void EnsureSlots(Entity<AttachableHolderComponent> holder)
    {
        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            _container.EnsureContainer<ContainerSlot>(holder, slotId);
        }
    }

    private List<string> GetValidSlots(Entity<AttachableHolderComponent> holder, EntityUid attachableUid)
    {
        var list = new List<string>();

        if (!HasComp<AttachableComponent>(attachableUid))
            return list;

        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            if (_whitelist.IsWhitelistPass(holder.Comp.Slots[slotId], attachableUid))
                list.Add(slotId);
        }

        return list;
    }

    public void ApplyModifiers(Entity<AttachableHolderComponent> holder, AttachableAlteredType attachableAltered)
    {
        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            if (!_container.TryGetContainer(holder, slotId, out var container) || container.Count <= 0)
                continue;

            var attachableUid = container.ContainedEntities[0];

            if (!TryComp<AttachableComponent>(attachableUid, out _))
                continue;

            var ev = new AttachableAlteredEvent(holder.Owner, attachableAltered);
            RaiseLocalEvent(attachableUid, ref ev);
        }
    }

    private void ToggleAttachable(EntityUid userUid, string slotId)
    {
        if (!TryComp<HandsComponent>(userUid, out var handsComponent) ||
            !TryComp<AttachableHolderComponent>(handsComponent.ActiveHandEntity, out var holderComponent))
        {
            return;
        }

        var active = handsComponent.ActiveHandEntity;
        if (!holderComponent.Running || !_actionBlocker.CanInteract(userUid, active))
            return;

        if (!_container.TryGetContainer(active.Value,
                slotId,
                out var container) || container.Count <= 0)
            return;

        var attachableUid = container.ContainedEntities[0];

        if (!HasComp<AttachableToggleableComponent>(attachableUid))
            return;

        var ev = new AttachableToggleStartedEvent((active.Value, holderComponent), userUid, slotId);
        RaiseLocalEvent(attachableUid, ref ev);
    }

    public void SetSupercedingAttachable(Entity<AttachableHolderComponent> holder, EntityUid? supercedingAttachable)
    {
        holder.Comp.SupercedingAttachable = supercedingAttachable;
    }

    public bool TryGetSlotId(EntityUid holderUid, EntityUid attachableUid, [NotNullWhen(true)] out string? slotId)
    {
        slotId = null;

        if (!TryComp<AttachableHolderComponent>(holderUid, out var holderComponent) ||
            !TryComp<AttachableComponent>(attachableUid, out _))
        {
            return false;
        }

        foreach (var id in holderComponent.Slots.Keys)
        {
            if (!_container.TryGetContainer(holderUid, id, out var container) || container.Count <= 0)
                continue;

            if (container.ContainedEntities[0] != attachableUid)
                continue;

            slotId = id;
            return true;
        }

        return false;
    }

    private void OnHolderGotUnequipped(Entity<AttachableHolderComponent> holder, ref GotUnequippedEvent args)
    {
        RevokeAttachableActions(holder, args.Equipee);
    }

    private void OnHolderGotUnequippedHand(Entity<AttachableHolderComponent> holder, ref GotUnequippedHandEvent args)
    {
        RevokeAttachableActions(holder, args.User);
    }

    private void RevokeAttachableActions(Entity<AttachableHolderComponent> holder, EntityUid performer)
    {
        foreach (var slotId in holder.Comp.Slots.Keys)
        {
            if (!_container.TryGetContainer(holder, slotId, out var container) ||
                container.Count <= 0)
            {
                continue;
            }

            var ent = container.ContainedEntities[0];
            if (!TryComp<AttachableToggleableComponent>(ent, out var toggleableComponent))
                continue;

            _attachableToggleable.RevokeAction((ent, toggleableComponent), performer);
        }
    }
}