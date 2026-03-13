using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Realmbox.UI.Controls
{
    /// <summary>
    /// Represents a Snackbar control that displays brief messages at the bottom of the screen.
    /// </summary>
    public partial class Snackbar : UserControl
    {
        /// <summary>
        /// The storyboard used to show the Snackbar.
        /// </summary>
        private readonly Storyboard _showStoryboard = new();

        /// <summary>
        /// The storyboard used to hide the Snackbar.
        /// </summary>
        private readonly Storyboard _hideStoryboard = new();

        /// <summary>
        /// Identifies the Message dependency property.
        /// </summary>
        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(Snackbar), new PropertyMetadata(string.Empty));

        /// <summary>
        /// Gets or sets the message to be displayed in the Snackbar.
        /// </summary>
        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Snackbar"/> class.
        /// </summary>
        public Snackbar()
        {
            InitializeComponent();
            SetupAnimations();
        }

        /// <summary>
        /// Sets up the animations for showing and hiding the Snackbar.
        /// </summary>
        private void SetupAnimations()
        {
            DoubleAnimation showAnimation = new()
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };

            DoubleAnimation hideAnimation = new()
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300)
            };

            // Add the animations to the storyboards
            _showStoryboard.Children.Add(showAnimation);
            _hideStoryboard.Children.Add(hideAnimation);

            // Set the target and property for the animations
            Storyboard.SetTarget(showAnimation, this);
            Storyboard.SetTarget(hideAnimation, this);
            Storyboard.SetTargetProperty(showAnimation, new PropertyPath(OpacityProperty));
            Storyboard.SetTargetProperty(hideAnimation, new PropertyPath(OpacityProperty));

            // Set the event handlers for the storyboards
            _hideStoryboard.Completed += (s, e) => Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Shows the Snackbar with the specified message for a given duration.
        /// </summary>
        /// <param name="message">The message to display.</param>
        /// <param name="durationMs">The duration in milliseconds to display the message. Default is 3000 milliseconds.</param>
        public void Show(string message, int durationMs = 3000)
        {
            Message = message;
            Visibility = Visibility.Visible;
            _showStoryboard.Begin();

            Task.Delay(durationMs).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => _hideStoryboard.Begin());
            });
        }
    }
}
