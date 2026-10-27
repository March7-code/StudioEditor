namespace StudioEditor.Rendering
{
    public static class ImportedRenderConfiguration
    {
        // The source LUT is part of the scene's authored colors. Its display scope
        // explicitly disables a second tone curve and post exposure in URP.
        public static bool ApplySourceColorGrading = true;
    }
}
