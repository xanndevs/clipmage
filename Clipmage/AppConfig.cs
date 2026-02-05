namespace Clipmage
{
    public static class AppConfig
    {

        // Drag & Drop Behavior
        public const int DRAG_WAIT_MS = 200;      // How long to hold still before file drag starts
        public const int DRAG_CANCEL_DISTANCE = 100; // Pixels to move to cancel the wait
        public const float DRAG_SCALE_FACTOR = 0.2f;
        public const float FILE_DRAG_OFFSET = 8;
        // Physics Constants 
        public const float FRICTION = 0.87f;
        public const float BOUNCE_FACTOR = 0.85f;
        public const float MIN_VELOCITY = 0.85f;
        public const int SMOOTHING_RANGE_MS = 14;

        public const int WINDOW_REFRESH_INTERVAL = 8; // ~120FPS animation calculation


        // Visual Defaults
        public const int BUTTON_CORNER_RADIUS = 4;
        public const int INTERACTION_BUTTON_SIZE = 32;
        public const int DRAG_WINDOW_MAXIMUM_WIDTH = 300;
        public const int DRAG_WINDOW_MAXIMUM_HEIGHT = 300;
    }
}