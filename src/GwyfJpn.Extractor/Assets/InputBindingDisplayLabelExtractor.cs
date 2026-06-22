using System;
using System.Collections.Generic;
using System.IO;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Extracts static display labels from Unity Input System binding paths stored in
/// serialized InputAction assets. The game displays these through
/// InputActionRebindingExtensions.GetBindingDisplayString.
/// </summary>
internal static class InputBindingDisplayLabelExtractor
{
    private static readonly Dictionary<string, string> BindingDisplayLabels = new(StringComparer.Ordinal)
    {
        ["<Gamepad>/buttonEast"] = "Button East",
        ["<Gamepad>/buttonNorth"] = "Button North",
        ["<Gamepad>/buttonSouth"] = "Button South",
        ["<Gamepad>/buttonWest"] = "Button West",
        ["<Gamepad>/dpad"] = "D-Pad",
        ["<Gamepad>/leftStick"] = "Left Stick",
        ["<Gamepad>/leftStick/down"] = "Left Stick Down",
        ["<Gamepad>/leftStick/left"] = "Left Stick Left",
        ["<Gamepad>/leftStick/right"] = "Left Stick Right",
        ["<Gamepad>/leftStick/up"] = "Left Stick Up",
        ["<Gamepad>/leftTrigger"] = "Left Trigger",
        ["<Gamepad>/rightShoulder"] = "Right Shoulder",
        ["<Gamepad>/rightStick"] = "Right Stick",
        ["<Gamepad>/rightStick/down"] = "Right Stick Down",
        ["<Gamepad>/rightStick/left"] = "Right Stick Left",
        ["<Gamepad>/rightStick/right"] = "Right Stick Right",
        ["<Gamepad>/rightStick/up"] = "Right Stick Up",
        ["<Gamepad>/rightStickPress"] = "Right Stick Press",
        ["<Gamepad>/rightTrigger"] = "Right Trigger",
        ["<Gamepad>/start"] = "Start",
        ["<Keyboard>/1"] = "1",
        ["<Keyboard>/2"] = "2",
        ["<Keyboard>/3"] = "3",
        ["<Keyboard>/a"] = "A",
        ["<Keyboard>/backquote"] = "Backquote",
        ["<Keyboard>/ctrl"] = "Control",
        ["<Keyboard>/d"] = "D",
        ["<Keyboard>/downArrow"] = "Down Arrow",
        ["<Keyboard>/e"] = "E",
        ["<Keyboard>/escape"] = "Escape",
        ["<Keyboard>/f1"] = "F1",
        ["<Keyboard>/f2"] = "F2",
        ["<Keyboard>/f3"] = "F3",
        ["<Keyboard>/f4"] = "F4",
        ["<Keyboard>/leftArrow"] = "Left Arrow",
        ["<Keyboard>/leftShift"] = "Shift",
        ["<Keyboard>/q"] = "Q",
        ["<Keyboard>/r"] = "R",
        ["<Keyboard>/rightArrow"] = "Right Arrow",
        ["<Keyboard>/s"] = "S",
        ["<Keyboard>/space"] = "Space",
        ["<Keyboard>/upArrow"] = "Up Arrow",
        ["<Keyboard>/v"] = "V",
        ["<Keyboard>/w"] = "W",
        ["<Mouse>/leftButton"] = "Left Button",
        ["<Mouse>/middleButton"] = "Middle Button",
        ["<Mouse>/rightButton"] = "Right Button",
        ["<Mouse>/scroll"] = "Scroll",
        ["<Mouse>/scroll/down"] = "Scroll Down",
        ["<Mouse>/scroll/up"] = "Scroll Up"
    };

    public static IEnumerable<CandidateEntry> Extract(string path, string relativeFile)
    {
        var bytes = File.ReadAllBytes(path);
        var serializedFile = UnitySerializedFile.Read(bytes);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var obj in serializedFile.Objects)
        {
            var objectStart = checked(serializedFile.DataOffset + obj.ByteStart);
            if (objectStart < 0 || objectStart + obj.ByteSize > bytes.LongLength || objectStart > int.MaxValue)
            {
                continue;
            }

            foreach (var serializedString in UnityLengthPrefixedStringScanner.Extract(bytes, (int)objectStart, obj.ByteSize))
            {
                if (!TryGetDisplayLabel(serializedString.Value, out var label))
                {
                    continue;
                }

                label = SourceTextClassifier.NormalizeCandidate(label);
                if (!SourceTextClassifier.IsMechanicallyReadableText(label))
                {
                    continue;
                }

                var key = relativeFile + "\n" + obj.PathId + "\n" + serializedString.Offset + "\n" + label;
                if (!seen.Add(key))
                {
                    continue;
                }

                yield return new CandidateEntry
                {
                    Id = $"input-binding-display:{relativeFile}:{obj.PathId}:{serializedString.Offset}:{StableId.Hash(label)}",
                    Source = label,
                    SourceKind = CandidateSourceKind.InputBindingDisplayLabel,
                    Context = new CandidateContext
                    {
                        File = relativeFile,
                        PathId = obj.PathId,
                        ClassId = obj.ClassId,
                        StringOffset = serializedString.Offset,
                        Type = "InputBinding"
                    }
                };
            }
        }
    }

    private static bool TryGetDisplayLabel(string bindingPath, out string label)
    {
        if (BindingDisplayLabels.TryGetValue(bindingPath.Trim(), out var mappedLabel))
        {
            label = mappedLabel;
            return true;
        }

        label = string.Empty;
        return false;
    }
}
