using System.Collections.Generic;
using Minis;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RewriteReality
{
    /// <summary>
    /// MIDI 入力（Minis / 新 Input System）を <see cref="ControlHub"/> へ橋渡しする（M7・docs/07 §2）。
    /// CC → 連続パラメータ（ラーンで任意 CC を任意パラメータへ）、Note-on → 割当エフェクトの ON/OFF。
    /// コントローラ非依存の原則どおり、CC/Note 番号の意味づけは ControlHub のマップだけが持つ。
    /// KeyboardControl と同じく opt-in（この MonoBehaviour を置いたときだけ有効）＝非破壊。
    ///
    /// Minis はチャンネルごとに <c>MidiDevice</c> を Input System デバイスとして生やす。ここでは
    /// デバイスの着脱（<see cref="InputSystem.onDeviceChange"/>）を監視して各 MidiDevice の
    /// will イベントを購読する。will イベントはコントロール状態の確定前に発火するため、
    /// 値はイベント引数の float（0..1）を使う（ReadValue は使わない・Minis 実装コメント準拠）。
    /// </summary>
    public sealed class MidiControl : MonoBehaviour
    {
        [SerializeField] ControlHub _hub;
        [Tooltip("受信内容を Console に出す（現場での配線確認用）")]
        [SerializeField] bool _log = false;

        readonly HashSet<MidiDevice> _hooked = new HashSet<MidiDevice>();

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            // 既に接続済みのデバイスを取りこぼさないよう初回に走査する。
            foreach (var d in InputSystem.devices) TryHook(d);
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var dev in _hooked) Unsubscribe(dev);
            _hooked.Clear();
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                    TryHook(device);
                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                    if (device is MidiDevice midi && _hooked.Remove(midi)) Unsubscribe(midi);
                    break;
            }
        }

        void TryHook(InputDevice device)
        {
            if (device is MidiDevice midi && _hooked.Add(midi))
            {
                midi.onWillControlChange += OnControlChange;
                midi.onWillNoteOn += OnNoteOn;
                midi.onWillNoteOff += OnNoteOff;
                if (_log) Debug.Log($"[MidiControl] hooked MIDI device: {midi.description.product} (ch {midi.channel})");
            }
        }

        void Unsubscribe(MidiDevice midi)
        {
            midi.onWillControlChange -= OnControlChange;
            midi.onWillNoteOn -= OnNoteOn;
            midi.onWillNoteOff -= OnNoteOff;
        }

        void OnControlChange(MidiValueControl control, float value)
        {
            if (_hub != null) _hub.ApplyMidiCc(control.controlNumber, value);
            if (_log) Debug.Log($"[MidiControl] CC {control.controlNumber} = {value:F3}");
        }

        void OnNoteOn(MidiNoteControl note, float velocity)
        {
            if (_hub != null) _hub.ApplyMidiNote(note.noteNumber, true);
            if (_log) Debug.Log($"[MidiControl] Note {note.noteNumber} on ({velocity:F2})");
        }

        void OnNoteOff(MidiNoteControl note)
        {
            if (_hub != null) _hub.ApplyMidiNote(note.noteNumber, false);
        }
    }
}
