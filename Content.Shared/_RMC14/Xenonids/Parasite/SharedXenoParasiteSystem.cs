﻿using Content.Shared._RMC14.Hands;
using Content.Shared._RMC14.Xenonids.Leap;
using Content.Shared._RMC14.Xenonids.Pheromones;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Parasite;

public abstract class SharedXenoParasiteSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly BlindableSystem _blindable = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly CMHandsSystem _cmHands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<InfectableComponent, ActivateInWorldEvent>(OnInfectableActivate);
        SubscribeLocalEvent<InfectableComponent, CanDropTargetEvent>(OnInfectableCanDropTarget);

        SubscribeLocalEvent<XenoParasiteComponent, XenoLeapHitEvent>(OnParasiteLeapHit);
        SubscribeLocalEvent<XenoParasiteComponent, AfterInteractEvent>(OnParasiteAfterInteract);
        SubscribeLocalEvent<XenoParasiteComponent, DoAfterAttemptEvent<AttachParasiteDoAfterEvent>>(OnParasiteAttachDoAfterAttempt);
        SubscribeLocalEvent<XenoParasiteComponent, AttachParasiteDoAfterEvent>(OnParasiteAttachDoAfter);
        SubscribeLocalEvent<XenoParasiteComponent, CanDragEvent>(OnParasiteCanDrag);
        SubscribeLocalEvent<XenoParasiteComponent, CanDropDraggedEvent>(OnParasiteCanDropDragged);
        SubscribeLocalEvent<XenoParasiteComponent, DragDropDraggedEvent>(OnParasiteDragDropDragged);

        SubscribeLocalEvent<ParasiteSpentComponent, MapInitEvent>(OnParasiteSpentMapInit);
        SubscribeLocalEvent<ParasiteSpentComponent, UpdateMobStateEvent>(OnParasiteSpentUpdateMobState,
            after: [typeof(MobThresholdSystem), typeof(SharedXenoPheromonesSystem)]);

        SubscribeLocalEvent<VictimInfectedComponent, MapInitEvent>(OnVictimInfectedMapInit);
        SubscribeLocalEvent<VictimInfectedComponent, ComponentRemove>(OnVictimInfectedRemoved);
        SubscribeLocalEvent<VictimInfectedComponent, CanSeeAttemptEvent>(OnVictimInfectedCancel);
        SubscribeLocalEvent<VictimInfectedComponent, ExaminedEvent>(OnVictimInfectedExamined);
        SubscribeLocalEvent<VictimInfectedComponent, RejuvenateEvent>(OnVictimInfectedRejuvenate);

        SubscribeLocalEvent<VictimBurstComponent, MapInitEvent>(OnVictimBurstMapInit);
        SubscribeLocalEvent<VictimBurstComponent, UpdateMobStateEvent>(OnVictimUpdateMobState,
            after: [typeof(MobThresholdSystem), typeof(SharedXenoPheromonesSystem)]);
        SubscribeLocalEvent<VictimBurstComponent, RejuvenateEvent>(OnVictimBurstRejuvenate);
    }

    private void OnInfectableActivate(Entity<InfectableComponent> ent, ref ActivateInWorldEvent args)
    {
        if (TryComp(args.User, out XenoParasiteComponent? parasite) &&
            StartInfect((args.User, parasite), args.Target, args.User))
        {
            args.Handled = true;
        }
    }

    private void OnInfectableCanDropTarget(Entity<InfectableComponent> ent, ref CanDropTargetEvent args)
    {
        if (TryComp(args.Dragged, out XenoParasiteComponent? parasite) &&
            CanInfectPopup((args.Dragged, parasite), ent, args.User, false))
        {
            args.CanDrop = true;
            args.Handled = true;
        }
    }

    private void OnParasiteLeapHit(Entity<XenoParasiteComponent> parasite, ref XenoLeapHitEvent args)
    {
        var coordinates = _transform.GetMoverCoordinates(parasite);
        if (_transform.InRange(coordinates, args.Leaping.Origin, parasite.Comp.InfectRange))
            Infect(parasite, args.Hit, false);
    }

    private void OnParasiteAfterInteract(Entity<XenoParasiteComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.CanReach || args.Target == null)
            return;

        if (StartInfect(ent, args.Target.Value, args.User))
            args.Handled = true;
    }

    private void OnParasiteAttachDoAfterAttempt(Entity<XenoParasiteComponent> ent, ref DoAfterAttemptEvent<AttachParasiteDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Target is not { } target)
        {
            args.Cancel();
            return;
        }

        if (!CanInfectPopup(ent, target, ent))
            args.Cancel();
    }

    private void OnParasiteAttachDoAfter(Entity<XenoParasiteComponent> ent, ref AttachParasiteDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null)
            return;

        if (Infect(ent, args.Target.Value))
            args.Handled = true;
    }

    private void OnParasiteCanDrag(Entity<XenoParasiteComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    private void OnParasiteCanDropDragged(Entity<XenoParasiteComponent> ent, ref CanDropDraggedEvent args)
    {
        if (args.User != ent.Owner && !_cmHands.IsPickupByAllowed(ent.Owner, args.User))
            return;

        if (!CanInfectPopup(ent, args.Target, args.User, false))
            return;

        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnParasiteDragDropDragged(Entity<XenoParasiteComponent> ent, ref DragDropDraggedEvent args)
    {
        if (args.User != ent.Owner && !_cmHands.IsPickupByAllowed(ent.Owner, args.User))
            return;

        StartInfect(ent, args.Target, args.User);
        args.Handled = true;
    }

    protected virtual void ParasiteLeapHit(Entity<XenoParasiteComponent> parasite)
    {
    }

    private void OnParasiteSpentMapInit(Entity<ParasiteSpentComponent> spent, ref MapInitEvent args)
    {
        if (TryComp(spent, out MobStateComponent? mobState))
            _mobState.UpdateMobState(spent, mobState);
    }

    private void OnParasiteSpentUpdateMobState(Entity<ParasiteSpentComponent> spent, ref UpdateMobStateEvent args)
    {
        args.State = MobState.Dead;
    }

    private void OnVictimInfectedMapInit(Entity<VictimInfectedComponent> victim, ref MapInitEvent args)
    {
        victim.Comp.FallOffAt = _timing.CurTime + victim.Comp.FallOffDelay;
        victim.Comp.BurstAt = _timing.CurTime + victim.Comp.BurstDelay;

        _appearance.SetData(victim, victim.Comp.InfectedLayer, true);
    }

    private void OnVictimInfectedRemoved(Entity<VictimInfectedComponent> victim, ref ComponentRemove args)
    {
        _blindable.UpdateIsBlind(victim.Owner);
        _standing.Stand(victim);
    }

    private void OnVictimInfectedCancel<T>(Entity<VictimInfectedComponent> victim, ref T args) where T : CancellableEntityEventArgs
    {
        if (victim.Comp.LifeStage <= ComponentLifeStage.Running && !victim.Comp.Recovered)
            args.Cancel();
    }

    private void OnVictimInfectedExamined(Entity<VictimInfectedComponent> victim, ref ExaminedEvent args)
    {
        if (HasComp<XenoComponent>(args.Examiner) || (CompOrNull<GhostComponent>(args.Examiner)?.CanGhostInteract ?? false))
            args.PushMarkup("This creature is impregnated.");
    }

    private void OnVictimInfectedRejuvenate(Entity<VictimInfectedComponent> victim, ref RejuvenateEvent args)
    {
        RemCompDeferred<VictimInfectedComponent>(victim);
    }

    private void OnVictimBurstMapInit(Entity<VictimBurstComponent> burst, ref MapInitEvent args)
    {
        _appearance.SetData(burst, burst.Comp.BurstLayer, true);

        if (TryComp(burst, out MobStateComponent? mobState))
            _mobState.UpdateMobState(burst, mobState);
    }

    private void OnVictimUpdateMobState(Entity<VictimBurstComponent> burst, ref UpdateMobStateEvent args)
    {
        args.State = MobState.Dead;
    }

    private void OnVictimBurstRejuvenate(Entity<VictimBurstComponent> burst, ref RejuvenateEvent args)
    {
        RemCompDeferred<VictimBurstComponent>(burst);
    }

    private bool StartInfect(Entity<XenoParasiteComponent> parasite, EntityUid victim, EntityUid user)
    {
        if (!CanInfectPopup(parasite, victim, user))
            return false;

        var ev = new AttachParasiteDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, user, parasite.Comp.ManualAttachDelay, ev, parasite, victim)
        {
            BreakOnMove = true,
            AttemptFrequency = AttemptFrequency.EveryTick
        };
        _doAfter.TryStartDoAfter(doAfter);

        return true;
    }

    private bool CanInfectPopup(Entity<XenoParasiteComponent> parasite, EntityUid victim, EntityUid user, bool popup = true, bool force = false)
    {
        if (!HasComp<InfectableComponent>(victim) ||
            HasComp<ParasiteSpentComponent>(parasite) ||
            HasComp<VictimInfectedComponent>(victim))
        {
            if (popup)
                _popup.PopupClient(Loc.GetString("cm-xeno-failed-cant-infect", ("target", victim)), victim, user, PopupType.MediumCaution);

            return false;
        }

        if (!force &&
            TryComp(victim, out StandingStateComponent? standing) &&
            !_standing.IsDown(victim, standing))
        {
            if (popup)
                _popup.PopupClient(Loc.GetString("cm-xeno-failed-cant-reach", ("target", victim)), victim, user, PopupType.MediumCaution);

            return false;
        }

        if (_mobState.IsDead(victim))
        {
            if (popup)
                _popup.PopupClient(Loc.GetString("cm-xeno-failed-target-dead"), victim, user, PopupType.MediumCaution);

            return false;
        }

        return true;
    }

    public bool Infect(Entity<XenoParasiteComponent> parasite, EntityUid victim, bool popup = true, bool force = false)
    {
        if (!CanInfectPopup(parasite, victim, parasite, popup, force))
            return false;

        if (_inventory.TryGetContainerSlotEnumerator(victim, out var slots, SlotFlags.MASK))
        {
            var any = false;
            while (slots.MoveNext(out var slot))
            {
                if (slot.ContainedEntity != null)
                {
                    _inventory.TryUnequip(victim, victim, slot.ID, force: true);
                    any = true;
                }
            }

            if (any && _net.IsServer)
            {
                _popup.PopupEntity(Loc.GetString("cm-xeno-infect-success", ("target", victim)), victim);
            }
        }

        if (_net.IsServer &&
            TryComp(victim, out InfectableComponent? infectable) &&
            TryComp(victim, out HumanoidAppearanceComponent? appearance) &&
            infectable.Sound.TryGetValue(appearance.Sex, out var sound))
        {
            var filter = Filter.Pvs(victim);
            _audio.PlayEntity(sound, filter, victim, true);
        }

        var time = _timing.CurTime;
        var victimComp = EnsureComp<VictimInfectedComponent>(victim);
        victimComp.AttachedAt = time;
        victimComp.RecoverAt = time + parasite.Comp.ParalyzeTime;
        victimComp.Hive = CompOrNull<XenoComponent>(parasite)?.Hive ?? default;
        _stun.TryParalyze(victim, parasite.Comp.ParalyzeTime, true);

        var container = _container.EnsureContainer<ContainerSlot>(victim, victimComp.ContainerId);
        _container.Insert(parasite.Owner, container);

        _blindable.UpdateIsBlind(victim);
        _appearance.SetData(parasite, victimComp.InfectedLayer, true);

        // TODO RMC14 also do damage to the parasite
        EnsureComp<ParasiteSpentComponent>(parasite);

        ParasiteLeapHit(parasite);
        return true;
    }

    public void RefreshIncubationMultipliers(Entity<VictimInfectedComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        var ev = new GetInfectedIncubationMultiplierEvent(1);
        RaiseLocalEvent(ent, ref ev);

        ent.Comp.IncubationMultiplier = ev.Multiplier;
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<VictimInfectedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var infected, out var xform))
        {
            if (infected.FallOffAt < time && !infected.FellOff)
            {
                infected.FellOff = true;
                _appearance.SetData(uid, infected.InfectedLayer, false);
                if (_container.TryGetContainer(uid, infected.ContainerId, out var container))
                    _container.EmptyContainer(container);
            }

            if (infected.RecoverAt < time && !infected.Recovered)
            {
                infected.Recovered = true;
                _blindable.UpdateIsBlind(uid);
            }

            if (_net.IsClient)
                continue;

            if (infected.BurstAt > time)
            {
                // TODO RMC14 make this less effective against late-stage infections, also make this support faster incubation
                if (infected.IncubationMultiplier < 1)
                    infected.BurstAt += TimeSpan.FromSeconds(1 - infected.IncubationMultiplier) * frameTime;

                continue;
            }

            RemCompDeferred<VictimInfectedComponent>(uid);

            var spawned = SpawnAtPosition(infected.BurstSpawn, xform.Coordinates);
            _xeno.SetHive(spawned, infected.Hive);

            EnsureComp<VictimBurstComponent>(uid);

            _audio.PlayPvs(infected.BurstSound, uid);
        }
    }
}
