namespace FrameWrite
{
    /// <summary>
    /// Represents the current state of the UI workflow.
    /// Used to determine which controls should be visible and what actions are available.
    /// </summary>
    public enum UIState
    {
        /// <summary>
        /// Initial state - wizard needs to be shown to configure app
        /// </summary>
        WizardNeeded,
        
        /// <summary>
        /// Configuration complete - user needs to select capture region
        /// </summary>
        SelectRegion,
        
        /// <summary>
        /// Region selected - ready to start capturing
        /// </summary>
        ReadyToCapture,
        
        /// <summary>
        /// Currently capturing frames
        /// </summary>
        Capturing,
        
        /// <summary>
        /// Capture stopped - has frames available for encoding
        /// </summary>
        ReadyToEncode
    }
}
