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

        // TextBox Behavior
        public const bool IS_TAB_TO_INDENT = true;
        public const int TAB_WIDTH = 4;




        // Visual Defaults
        public const int BUTTON_CORNER_RADIUS = 5;
        public const int WINDOW_CORNER_RADIUS = 8;
        public const int INTERACTION_BUTTON_SIZE = 32;
        public const int DRAG_WINDOW_MAXIMUM_WIDTH = 300;
        public const int DRAG_WINDOW_MAXIMUM_HEIGHT = 300;

        public const int TEXTBOX_FONT_SIZE = 12;

        public const int SHELF_IMAGE_HEIGHT = 80;

        public const int PADDING_SMALL = 4;
        public const int PADDING_NORMAL = 8;
        public const int PADDING_LARGE = 16;
    }
}