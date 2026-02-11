namespace Clipmage
{
    public static class AppConfig
    {

        // Drag & Drop Behavior
        public const int DRAG_WAIT_MS = 250;      // How long to hold still before file drag starts
        public const int DRAG_CANCEL_DISTANCE = 90; // Pixels to move to cancel the wait
        public const float DRAG_SCALE_FACTOR = 0.16f;
        public const float FILE_DRAG_OFFSET = 8;
        // Physics Constants 
        public const float FRICTION = 0.87f;
        public const float BOUNCE_FACTOR = 1.45f;
        public const float MIN_VELOCITY = 0.85f;
        public const float MAX_VELOCITY = 60f;
        public const int SMOOTHING_RANGE_MS = 14;

        public const int WINDOW_REFRESH_INTERVAL = 8; // ~120FPS animation calculation

        // TextBox Behavior
        public const bool IS_TAB_TO_INDENT = true;
        public const int TAB_WIDTH = 4;

        // Animation Constants
        public const double FADE_OUT_PERCENT = 0.025;


        // Visual Defaults
        public const int BUTTON_CORNER_RADIUS = 5;
        public const int WINDOW_CORNER_RADIUS = 8;
        public const int DIALOG_BUTTON_SIZE = 24;
        public const int INTERACTION_BUTTON_SIZE = 32;
        public const int DRAG_WINDOW_MAXIMUM_WIDTH = 300;
        public const int DRAG_WINDOW_MAXIMUM_HEIGHT = 300;

        public const int FONT_SIZE_TINY = 8;
        public const int FONT_SIZE_SMALL = 10;
        public const int FONT_SIZE_NORMAL = 12;
        public const int FONT_SIZE_LARGE = 14;

        public const int SHELF_IMAGE_HEIGHT = 80;

        public const int PADDING_TINY = 2;
        public const int PADDING_SMALL = 4;
        public const int PADDING_NORMAL = 8;
        public const int PADDING_LARGE = 16;

        //BOUNCY WINDOW PRESET
        //public const float FRICTION = 0.99f;
        //public const float BOUNCE_FACTOR = 1.45f;
        //public const float MIN_VELOCITY = 0.85f;
        //public const float MAX_VELOCITY = 90f;
    }
}