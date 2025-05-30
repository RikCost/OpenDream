﻿using System.Text;
using OpenDreamRuntime.Map;
using OpenDreamRuntime.Objects.Types;
using OpenDreamShared.Dream;
using OpenDreamShared.Input;
using Robust.Shared.Timing;

namespace OpenDreamRuntime.Input;

internal sealed class MouseInputSystem : SharedMouseInputSystem {
    [Dependency] private readonly AtomManager _atomManager = default!;
    [Dependency] private readonly DreamManager _dreamManager = default!;
    [Dependency] private readonly IDreamMapManager _mapManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly TimeSpan _doubleClickDelay = TimeSpan.FromMilliseconds(250);

    public override void Initialize() {
        base.Initialize();

        SubscribeNetworkEvent<AtomClickedEvent>(OnAtomClicked);
        SubscribeNetworkEvent<AtomDraggedEvent>(OnAtomDragged);
        SubscribeNetworkEvent<StatClickedEvent>(OnStatClicked);
        SubscribeNetworkEvent<MouseEnteredEvent>(OnMouseEntered);
        SubscribeNetworkEvent<MouseExitedEvent>(OnMouseExited);
        SubscribeNetworkEvent<MouseMoveEvent>(OnMouseMove);
    }

    private void OnAtomClicked(AtomClickedEvent e, EntitySessionEventArgs sessionEvent) {
        var connection = _dreamManager.GetConnectionBySession(sessionEvent.SenderSession);
        var clicked = _dreamManager.GetFromClientReference(connection, e.ClickedAtom);
        if (clicked is not DreamObjectAtom atom)
            return;

        HandleAtomClick(e, atom, sessionEvent);
    }

    private void OnAtomDragged(AtomDraggedEvent e, EntitySessionEventArgs sessionEvent) {
        var connection = _dreamManager.GetConnectionBySession(sessionEvent.SenderSession);
        var src = _dreamManager.GetFromClientReference(connection, e.SrcAtom);
        if (src is not DreamObjectAtom srcAtom)
            return;

        var usr = connection.Mob;
        var srcPos = _atomManager.GetAtomPosition(srcAtom);
        var over = (e.OverAtom != null)
            ? _dreamManager.GetFromClientReference(connection, e.OverAtom.Value) as DreamObjectAtom
            : null;

        _mapManager.TryGetTurfAt((srcPos.X, srcPos.Y), srcPos.Z, out var srcLoc);

        DreamValue overLocValue = DreamValue.Null;
        if (over != null) {
            var overPos = _atomManager.GetAtomPosition(over);

            _mapManager.TryGetTurfAt((overPos.X, overPos.Y), overPos.Z, out var overLoc);
            overLocValue = new(overLoc);
        }

        connection.Client?.SpawnProc("MouseDrop", usr: usr,
            new DreamValue(src),
            new DreamValue(over),
            new DreamValue(srcLoc), // TODO: Location can be a skin element
            overLocValue,
            DreamValue.Null, // TODO: src_control and over_control
            DreamValue.Null,
            new DreamValue(ConstructClickParams(e.Params)));
    }

    private void OnStatClicked(StatClickedEvent e, EntitySessionEventArgs sessionEvent) {
        if (!_dreamManager.LocateRef(e.AtomRef).TryGetValueAsDreamObject<DreamObjectAtom>(out var dreamObject))
            return;

        HandleAtomClick(e, dreamObject, sessionEvent);
    }

    private void OnMouseEntered(MouseEnteredEvent e, EntitySessionEventArgs sessionEvent) {
        var connection = _dreamManager.GetConnectionBySession(sessionEvent.SenderSession);
        var atom = _dreamManager.GetFromClientReference(connection, e.Atom);
        if (atom is not DreamObjectAtom)
            return;
        if (!_atomManager.GetEnabledMouseEvents(atom).HasFlag(AtomMouseEvents.Enter))
            return;

        atom.SpawnProc("MouseEntered", usr: connection.Mob,
            atom.GetVariable("loc"),
            DreamValue.Null,
            new DreamValue(ConstructClickParams(e.Params)));
    }

    private void OnMouseExited(MouseExitedEvent e, EntitySessionEventArgs sessionEvent) {
        var connection = _dreamManager.GetConnectionBySession(sessionEvent.SenderSession);
        var atom = _dreamManager.GetFromClientReference(connection, e.Atom);
        if (atom is not DreamObjectAtom)
            return;
        if (!_atomManager.GetEnabledMouseEvents(atom).HasFlag(AtomMouseEvents.Exit))
            return;

        atom.SpawnProc("MouseExited", usr: connection.Mob,
            atom.GetVariable("loc"),
            DreamValue.Null,
            new DreamValue(ConstructClickParams(e.Params)));
    }

    private void OnMouseMove(MouseMoveEvent e, EntitySessionEventArgs sessionEvent) {
        var connection = _dreamManager.GetConnectionBySession(sessionEvent.SenderSession);
        var atom = _dreamManager.GetFromClientReference(connection, e.Atom);
        if (atom is not DreamObjectAtom)
            return;
        if (!_atomManager.GetEnabledMouseEvents(atom).HasFlag(AtomMouseEvents.Move))
            return;

        atom.SpawnProc("MouseMove", usr: connection.Mob,
            atom.GetVariable("loc"),
            DreamValue.Null,
            new DreamValue(ConstructClickParams(e.Params)));
    }

    private void HandleAtomClick(IAtomMouseEvent e, DreamObjectAtom atom, EntitySessionEventArgs sessionEvent) {
        var session = sessionEvent.SenderSession;
        var connection = _dreamManager.GetConnectionBySession(session);
        var usr = connection.Mob;

        var clickParams = ConstructClickParams(e.Params);

        // Double click fires before the second Click() fires
        if (_timing.RealTime - connection.LastClickTime <= _doubleClickDelay) {
            connection.Client?.SpawnProc("DblClick", usr: usr,
                new DreamValue(atom),
                DreamValue.Null,
                DreamValue.Null,
                new DreamValue(clickParams));
        }

        connection.Client?.SpawnProc("Click", usr: usr,
            new DreamValue(atom),
            DreamValue.Null,
            DreamValue.Null,
            new DreamValue(clickParams));

        connection.LastClickTime = _timing.RealTime;
    }

    private string ConstructClickParams(ClickParams clickParams) {
        StringBuilder paramsBuilder = new StringBuilder(96); // Click param strings are typically ~86 chars with all modifiers held. 96 is 64*1.5

        // All of these parameters have been ordered with BYOND parity

        paramsBuilder.Append($"icon-x={clickParams.IconX.ToString()};");
        paramsBuilder.Append($"icon-y={clickParams.IconY.ToString()};");

        string button;

        // Handles setting left=1, right=1, or middle=1 mouse param
        if (clickParams.Right) {
            paramsBuilder.Append("right=1;");
            button = "right";
        } else if (clickParams.Middle) {
             paramsBuilder.Append("middle=1;");
             button = "middle";
        } else {
            paramsBuilder.Append("left=1;");
            button = "left";
        }

        // Modifier keys
        if (clickParams.Ctrl) paramsBuilder.Append("ctrl=1;");
        if (clickParams.Shift) paramsBuilder.Append("shift=1;");
        if (clickParams.Alt) paramsBuilder.Append("alt=1;");

        paramsBuilder.Append($"button={button};");

        // Screen loc
        paramsBuilder.Append($"screen-loc={clickParams.ScreenLoc.ToCoordinates()}");

        return paramsBuilder.ToString();
    }
}
