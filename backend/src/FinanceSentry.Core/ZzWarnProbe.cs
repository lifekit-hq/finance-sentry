namespace FinanceSentry.Core;

// Deliberate CS0219 (variable assigned but never used). This file exists only to prove in CI that
// the warnings-as-errors gate from #1463 fails `build-and-test`. The branch is closed, never merged.
internal static class ZzWarnProbe
{
    internal static void Touch()
    {
        var unused = 1;
    }
}
