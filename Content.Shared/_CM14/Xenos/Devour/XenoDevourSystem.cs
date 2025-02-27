﻿using System.Diagnostics.CodeAnalysis;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Hands;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._CM14.Xenos.Devour;

public sealed class XenoDevourSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DevourableComponent, CanDropDraggedEvent>(OnDevourableCanDropDragged);
        SubscribeLocalEvent<DevourableComponent, DragDropDraggedEvent>(OnDevourableDragDropDragged);
        SubscribeLocalEvent<DevourableComponent, BeforeRangedInteractEvent>(OnDevourableBeforeRangedInteract);

        SubscribeLocalEvent<DevouredComponent, ComponentStartup>(OnDevouredStartup);
        SubscribeLocalEvent<DevouredComponent, ComponentRemove>(OnDevouredRemove);
        SubscribeLocalEvent<DevouredComponent, EntGotRemovedFromContainerMessage>(OnDevouredRemovedFromContainer);
        SubscribeLocalEvent<DevouredComponent, UpdateCanMoveEvent>(OnDevouredAttempt);
        SubscribeLocalEvent<DevouredComponent, InteractionAttemptEvent>(OnDevouredAttempt);
        SubscribeLocalEvent<DevouredComponent, UseAttemptEvent>(OnDevouredAttempt);
        SubscribeLocalEvent<DevouredComponent, ThrowAttemptEvent>(OnDevouredAttempt);
        SubscribeLocalEvent<DevouredComponent, DropAttemptEvent>(OnDevouredAttempt);
        SubscribeLocalEvent<DevouredComponent, PickupAttemptEvent>(OnDevouredPickupAttempt);
        SubscribeLocalEvent<DevouredComponent, IsEquippingAttemptEvent>(OnDevouredIsEquippingAttempt);
        SubscribeLocalEvent<DevouredComponent, IsUnequippingAttemptEvent>(OnDevouredIsUnequippingAttempt);
        SubscribeLocalEvent<DevouredComponent, AttackAttemptEvent>(OnDevouredAttackAttempt);

        SubscribeLocalEvent<XenoDevourComponent, CanDropTargetEvent>(OnXenoCanDropTarget);
        SubscribeLocalEvent<XenoDevourComponent, ActivateInWorldEvent>(OnXenoActivate);
        SubscribeLocalEvent<XenoDevourComponent, DoAfterAttemptEvent<XenoDevourDoAfterEvent>>(OnXenoDevourDoAfterAttempt);
        SubscribeLocalEvent<XenoDevourComponent, XenoDevourDoAfterEvent>(OnXenoDevourDoAfter);
        SubscribeLocalEvent<XenoDevourComponent, XenoRegurgitateActionEvent>(OnXenoRegurgitateAction);
        SubscribeLocalEvent<XenoDevourComponent, EntityTerminatingEvent>(OnXenoTerminating);
        SubscribeLocalEvent<XenoDevourComponent, MobStateChangedEvent>(OnXenoMobStateChanged);
    }

    private void OnDevourableCanDropDragged(Entity<DevourableComponent> devourable, ref CanDropDraggedEvent args)
    {
        if (HasComp<XenoDevourComponent>(args.User))
        {
            args.CanDrop = true;
            args.Handled = true;
        }
    }

    private void OnDevourableDragDropDragged(Entity<DevourableComponent> devourable, ref DragDropDraggedEvent args)
    {
        if (args.User != args.Target)
            return;

        if (StartDevour(args.User, devourable))
            args.Handled = true;
    }

    private void OnDevourableBeforeRangedInteract(Entity<DevourableComponent> ent, ref BeforeRangedInteractEvent args)
    {
        if (args.User != args.Target)
            return;

        if (StartDevourPulled(args.User))
            args.Handled = true;
    }

    private void OnDevouredStartup(Entity<DevouredComponent> devoured, ref ComponentStartup args)
    {
        _blocker.UpdateCanMove(devoured);
    }

    private void OnDevouredRemove(Entity<DevouredComponent> devoured, ref ComponentRemove args)
    {
        _blocker.UpdateCanMove(devoured);

        if (_timing.ApplyingState)
            return;

        if (_container.TryGetContainingContainer((devoured, null), out var container) &&
            TryComp(container.Owner, out XenoDevourComponent? devour) &&
            container.ID != devour.DevourContainerId)
        {
            _container.Remove(devoured.Owner, container);
        }
    }

    private void OnDevouredRemovedFromContainer(Entity<DevouredComponent> devoured, ref EntGotRemovedFromContainerMessage args)
    {
        if (!_timing.ApplyingState)
            RemCompDeferred<DevouredComponent>(devoured);
    }

    private void OnDevouredAttempt<T>(Entity<DevouredComponent> devoured, ref T args) where T : CancellableEntityEventArgs
    {
        args.Cancel();
    }

    private void OnDevouredAttackAttempt(Entity<DevouredComponent> devoured, ref AttackAttemptEvent args)
    {
        if (!HasComp<UsableWhileDevouredComponent>(args.Weapon))
            args.Cancel();
    }

    private void OnDevouredPickupAttempt(Entity<DevouredComponent> ent, ref PickupAttemptEvent args)
    {
        if (!HasComp<UsableWhileDevouredComponent>(args.Item))
            args.Cancel();
    }

    private void OnDevouredIsEquippingAttempt(Entity<DevouredComponent> devoured, ref IsEquippingAttemptEvent args)
    {
        if (!HasComp<UsableWhileDevouredComponent>(args.Equipment))
            args.Cancel();
    }

    private void OnDevouredIsUnequippingAttempt(Entity<DevouredComponent> devoured, ref IsUnequippingAttemptEvent args)
    {
        if (!HasComp<UsableWhileDevouredComponent>(args.Equipment))
            args.Cancel();
    }

    private void OnXenoCanDropTarget(Entity<XenoDevourComponent> xeno, ref CanDropTargetEvent args)
    {
        if (HasComp<DevourableComponent>(args.Dragged) && xeno.Owner == args.User)
            args.CanDrop = true;

        args.Handled = true;
    }

    private void OnXenoActivate(Entity<XenoDevourComponent> xeno, ref ActivateInWorldEvent args)
    {
        if (args.User != args.Target)
            return;

        if (StartDevourPulled(args.User))
            args.Handled = true;
    }

    private void OnXenoDevourDoAfterAttempt(Entity<XenoDevourComponent> ent, ref DoAfterAttemptEvent<XenoDevourDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Target is not { } target ||
            !CanDevour(ent, target, out _, true))
        {
            args.Cancel();
        }
    }

    private void OnXenoDevourDoAfter(Entity<XenoDevourComponent> xeno, ref XenoDevourDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!CanDevour(xeno, target, out _, true))
            return;

        args.Handled = true;

        var container = _container.EnsureContainer<ContainerSlot>(xeno, xeno.Comp.DevourContainerId);
        if (!_container.Insert(target, container))
        {
            _popup.PopupClient($"You can't devour {target}!", xeno, xeno, PopupType.SmallCaution);
            return;
        }

        var devoured = EnsureComp<DevouredComponent>(target);
        devoured.WarnAt = _timing.CurTime + xeno.Comp.WarnAfter;
        devoured.RegurgitateAt = _timing.CurTime + xeno.Comp.RegurgitateAfter;

        var targetName = Identity.Name(target, EntityManager, xeno);
        _popup.PopupClient($"We devour {targetName}!", xeno, xeno, PopupType.Medium);

        var xenoName = Identity.Name(xeno, EntityManager, target);
        _popup.PopupEntity($"{xenoName} devours you!", xeno, target, PopupType.MediumCaution);

        var others = Filter.PvsExcept(xeno).RemovePlayerByAttachedEntity(target);
        foreach (var session in others.Recipients)
        {
            if (session.AttachedEntity is not { } recipient)
                continue;

            xenoName = Identity.Name(xeno, EntityManager, recipient);
            targetName = Identity.Name(target, EntityManager, recipient);
            _popup.PopupEntity($"{xenoName} devours {targetName}!", xeno, recipient, PopupType.MediumCaution);
        }
    }

    private void OnXenoRegurgitateAction(Entity<XenoDevourComponent> xeno, ref XenoRegurgitateActionEvent args)
    {
        if (!_container.TryGetContainer(xeno, xeno.Comp.DevourContainerId, out var container) ||
            container.ContainedEntities.Count == 0)
        {
            _popup.PopupClient(Loc.GetString("cm-xeno-none-devoured"), xeno, xeno);
            return;
        }

        args.Handled = true;
        _container.EmptyContainer(container);
        _popup.PopupClient("We hurl out the contents of our stomach!", xeno, xeno, PopupType.MediumCaution);
        _audio.PlayPredicted(xeno.Comp.RegurgitateSound, xeno, xeno);
    }

    private void OnXenoTerminating(Entity<XenoDevourComponent> xeno, ref EntityTerminatingEvent args)
    {
        if (_timing.ApplyingState)
            return;

        RegurgitateAll(xeno);
    }

    private void OnXenoMobStateChanged(Entity<XenoDevourComponent> xeno, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        RegurgitateAll(xeno);
    }

    private bool CanDevour(EntityUid xeno, EntityUid victim, [NotNullWhen(true)] out XenoDevourComponent? devour, bool popup = false)
    {
        devour = default;
        if (xeno == victim ||
            !TryComp(xeno, out devour) ||
            HasComp<DevouredComponent>(victim) ||
            !HasComp<DevourableComponent>(victim))
        {
            return false;
        }

        if (_mobState.IsIncapacitated(xeno))
        {
            if (popup)
                _popup.PopupClient("You can't do that right now!", victim, xeno);

            return false;
        }

        if (HasComp<XenoComponent>(victim))
        {
            if (popup)
                _popup.PopupClient("That wouldn't taste very good.", victim, xeno);

            return false;
        }

        if (_mobState.IsDead(victim))
        {
            if (popup)
            {
                var victimName = Identity.Name(victim, EntityManager, xeno);
                _popup.PopupClient($"Ew, {victimName} is already starting to rot.", victim, xeno);
            }

            return false;
        }

        if (_container.TryGetContainer(xeno, devour.DevourContainerId, out var container) &&
            container.ContainedEntities.Count > 0)
        {
            devour = null;

            if (popup)
                _popup.PopupClient("You already have something in your belly, there's no way that will fit!", victim, xeno, PopupType.SmallCaution);

            return false;
        }

        if (!_standing.IsDown(victim))
        {
            if (popup)
            {
                var victimName = Identity.Name(victim, EntityManager, xeno);
                _popup.PopupClient($"{victimName} is resisting, ground them!", victim, xeno, PopupType.MediumCaution);
            }

            return false;
        }

        if (TryComp(victim, out BuckleComponent? buckle) && buckle.BuckledTo is { } strap)
        {
            if (popup)
            {
                var victimName = Identity.Name(victim, EntityManager, xeno);
                var strapName = Loc.GetString("zzzz-the", ("ent", strap));
                _popup.PopupClient($"{victimName} is buckled to {strapName}.", victim, xeno);
            }
        }

        return true;
    }

    private bool StartDevour(EntityUid xeno, EntityUid target)
    {
        if (!CanDevour(xeno, target, out var devour, true))
            return false;

        var doAfter = new DoAfterArgs(EntityManager, xeno, devour.DevourDelay, new XenoDevourDoAfterEvent(), xeno, target)
        {
            BreakOnMove = true,
            AttemptFrequency = AttemptFrequency.EveryTick
        };

        var targetName = Identity.Name(target, EntityManager, xeno);
        _popup.PopupClient($"We start to devour {targetName}", target, xeno);

        var xenoName = Identity.Name(xeno, EntityManager, target);
        _popup.PopupEntity($"{xenoName} is trying to devour you!", xeno, target, PopupType.MediumCaution);

        var others = Filter.PvsExcept(xeno).RemovePlayerByAttachedEntity(target);
        foreach (var session in others.Recipients)
        {
            if (session.AttachedEntity is not { } recipient)
                continue;

            xenoName = Identity.Name(xeno, EntityManager, recipient);
            targetName = Identity.Name(target, EntityManager, recipient);
            _popup.PopupEntity($"{xenoName} starts to devour {targetName}!", target, recipient, PopupType.SmallCaution);
        }

        _doAfter.TryStartDoAfter(doAfter);
        return true;
    }

    private bool StartDevourPulled(EntityUid xeno)
    {
        if (CompOrNull<PullerComponent>(xeno)?.Pulling is not { } pulling)
            return false;

        return StartDevour(xeno, pulling);
    }

    private bool Regurgitate(Entity<DevouredComponent> devoured, Entity<XenoDevourComponent?> xeno, bool doFeedback = true)
    {
        if (!Resolve(xeno, ref xeno.Comp))
            return true;

        if (!_container.TryGetContainer(xeno, xeno.Comp.DevourContainerId, out var container) ||
            !_container.Remove(devoured.Owner, container))
        {
            return true;
        }

        if (doFeedback)
            DoFeedback((xeno, xeno.Comp));

        return false;
    }

    private void RegurgitateAll(Entity<XenoDevourComponent> xeno)
    {
        if (!_container.TryGetContainer(xeno, xeno.Comp.DevourContainerId, out var container))
            return;

        var any = false;
        foreach (var contained in container.ContainedEntities)
        {
            if (TryComp(contained, out DevouredComponent? devoured) &&
                Regurgitate((contained, devoured), (xeno, xeno), false))
            {
                any = true;
            }
        }

        if (any)
            DoFeedback(xeno);
    }

    private void DoFeedback(Entity<XenoDevourComponent> xeno)
    {
        _popup.PopupClient("We hurl out the contents of our stomach!", xeno, xeno, PopupType.MediumCaution);
        _audio.PlayPredicted(xeno.Comp.RegurgitateSound, xeno, xeno);
    }

    public override void Update(float frameTime)
    {
        var time = _timing.CurTime;
        var devoured = EntityQueryEnumerator<DevouredComponent, TransformComponent>();
        while (devoured.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!_container.TryGetContainingContainer((uid, xform), out var container) ||
                !TryComp(container.Owner, out XenoDevourComponent? devour) ||
                container.ID != devour.DevourContainerId)
            {
                RemCompDeferred<DevouredComponent>(uid);
                continue;
            }

            var xeno = container.Owner;
            if (_mobState.IsDead(uid))
            {
                Regurgitate((uid, comp), (xeno, devour));
                continue;
            }

            if (!comp.Warned && time >= comp.WarnAt)
            {
                comp.Warned = true;
                var victimName = Identity.Name(uid, EntityManager, xeno);
                _popup.PopupClient($"We're about to regurgitate {victimName}...", xeno, xeno, PopupType.MediumCaution);
            }

            if (time >= comp.RegurgitateAt)
            {
                if (Regurgitate((uid, comp), (xeno, devour)))
                    _popup.PopupClient("We hurl out the contents of our stomach!", xeno, xeno, PopupType.MediumCaution);
            }
        }
    }
}
