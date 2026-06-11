using System.Windows.Automation;

namespace OpenTranslate.Services;

public static class UiAutomationTextService
{
    public static TextCaptureResult? TryCaptureSelection(nint window, nint control)
    {
        try
        {
            foreach (var element in EnumerateCandidateElements(window, control))
            {
                var fromSelection = TryCaptureSelectionFromTextPattern(element, control);
                if (fromSelection is not null)
                    return fromSelection;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static TextCaptureResult? TryCapture(nint window, nint control)
    {
        try
        {
            foreach (var element in EnumerateCandidateElements(window, control))
            {
                var fromTextPattern = TryCaptureFromTextPattern(element, control);
                if (fromTextPattern is not null)
                    return fromTextPattern;

                var fromValuePattern = TryCaptureFromValuePattern(element, control);
                if (fromValuePattern is not null)
                    return fromValuePattern;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static bool? TryGetEditability(nint window, nint control)
    {
        try
        {
            foreach (var element in EnumerateCandidateElements(window, control))
            {
                var editability = GetEditabilityFromElement(element);
                if (editability.HasValue)
                    return editability;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static bool TrySetValue(nint window, nint control, string value)
    {
        try
        {
            foreach (var element in EnumerateCandidateElements(window, control))
            {
                if (TryApplyWithValuePattern(element, value))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static bool TryApplyTranslation(nint window, nint control, string translated, bool replaceAll)
    {
        if (!replaceAll || translated.Contains('\n'))
            return false;

        try
        {
            foreach (var element in EnumerateCandidateElements(window, control))
            {
                if (TryApplyWithValuePattern(element, translated))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static IEnumerable<AutomationElement> EnumerateCandidateElements(nint window, nint control)
    {
        var seen = new HashSet<string>();
        var roots = ResolveRoots(window, control).Where(r => r is not null).Cast<AutomationElement>();

        foreach (var root in roots)
        {
            if (TryAddElement(seen, root))
                yield return root;

            AutomationElement? focusedDescendant = null;
            try
            {
                focusedDescendant = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
            }
            catch
            {
                // Ignore tree walk failures in Electron apps.
            }

            if (TryAddElement(seen, focusedDescendant))
                yield return focusedDescendant!;

            AutomationElementCollection? editableNodes = null;
            try
            {
                editableNodes = root.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)));
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement candidate in editableNodes)
            {
                if (!TryAddElement(seen, candidate))
                    continue;

                if (!candidate.Current.IsEnabled)
                    continue;

                yield return candidate;
            }
        }
    }

    private static IEnumerable<AutomationElement?> ResolveRoots(nint window, nint control)
    {
        if (control != 0)
        {
            AutomationElement? fromControl = null;
            try
            {
                fromControl = AutomationElement.FromHandle(control);
            }
            catch
            {
                // Chromium/Electron often exposes the document via focused element instead.
            }

            if (fromControl is not null)
                yield return fromControl;
        }

        AutomationElement? focused = null;
        try
        {
            focused = AutomationElement.FocusedElement;
        }
        catch
        {
            // Ignore focus lookup failures.
        }

        if (focused is not null)
            yield return focused;

        if (window != 0)
        {
            AutomationElement? fromWindow = null;
            try
            {
                fromWindow = AutomationElement.FromHandle(window);
            }
            catch
            {
                // Ignore window lookup failures.
            }

            if (fromWindow is not null)
                yield return fromWindow;
        }
    }

    private static bool TryAddElement(HashSet<string> seen, AutomationElement? element)
    {
        if (element is null)
            return false;

        string key;
        try
        {
            key = element.Current.AutomationId + "|" + element.Current.ClassName + "|" + element.Current.Name;
        }
        catch
        {
            return false;
        }

        return seen.Add(key);
    }

    private static TextCaptureResult? TryCaptureSelectionFromTextPattern(AutomationElement element, nint control)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject))
            return null;

        var textPattern = (TextPattern)patternObject;
        var selections = textPattern.GetSelection();
        if (selections.Length == 0)
            return null;

        var selected = selections[0].GetText(int.MaxValue);
        if (string.IsNullOrWhiteSpace(selected))
            return null;

        return new TextCaptureResult
        {
            Text = TextFormattingHelper.NormalizeForTranslation(selected),
            Control = control,
            ReplaceAll = false
        };
    }

    private static TextCaptureResult? TryCaptureFromTextPattern(AutomationElement element, nint control)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject))
            return null;

        var fromSelection = TryCaptureSelectionFromTextPattern(element, control);
        if (fromSelection is not null)
            return fromSelection;

        var textPattern = (TextPattern)patternObject;
        var documentText = textPattern.DocumentRange.GetText(int.MaxValue);
        if (string.IsNullOrWhiteSpace(documentText))
            return null;

        return new TextCaptureResult
        {
            Text = TextFormattingHelper.NormalizeForTranslation(documentText),
            Control = control,
            ReplaceAll = true
        };
    }

    private static TextCaptureResult? TryCaptureFromValuePattern(AutomationElement element, nint control)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject))
            return null;

        if (patternObject is not ValuePattern valuePattern)
            return null;

        var value = valuePattern.Current.Value;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return new TextCaptureResult
        {
            Text = TextFormattingHelper.NormalizeForTranslation(value),
            Control = control,
            ReplaceAll = true
        };
    }

    private static bool? GetEditabilityFromElement(AutomationElement element)
    {
        if (!element.Current.IsEnabled)
            return false;

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject)
            && patternObject is ValuePattern valuePattern)
            return !valuePattern.Current.IsReadOnly;

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
        {
            var controlType = element.Current.ControlType;
            if (controlType == ControlType.Edit)
                return true;

            if (controlType == ControlType.Document)
                return false;
        }

        return null;
    }

    private static bool TryApplyWithValuePattern(AutomationElement element, string translated)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject))
            return false;

        if (patternObject is not ValuePattern valuePattern)
            return false;

        if (valuePattern.Current.IsReadOnly)
            return false;

        valuePattern.SetValue(translated);
        return true;
    }
}
