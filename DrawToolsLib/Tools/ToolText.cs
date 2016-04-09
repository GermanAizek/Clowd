using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Diagnostics;
using DrawToolsLib.Graphics;


namespace DrawToolsLib
{
    /// <summary>
    /// Text tool
    /// </summary>
    class ToolText : ToolRectangleBase
    {
        TextBox _textBox;
        string oldText;

        GraphicsVisual editedGraphicsText;
        DrawingCanvas drawingCanvas;


        public ToolText(DrawingCanvas drawingCanvas)
        {
            this.drawingCanvas = drawingCanvas;
            MemoryStream stream = new MemoryStream(Properties.Resources.Text);
            ToolCursor = new Cursor(stream);
        }

        /// <summary>
        /// Textbox is exposed for DrawingCanvas Visual Children Overrides.
        /// If it is not null, overrides should include this textbox.
        /// </summary>
        public TextBox TextBox
        {
            get { return _textBox; }
            set { _textBox = value; }
        }

        /// <summary>
        /// Create new text object
        /// </summary>
        public override void OnMouseDown(DrawingCanvas drawingCanvas, MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(drawingCanvas);
            AddNewObject(drawingCanvas, new GraphicsText(drawingCanvas, point));
        }

        /// <summary>
        /// Left mouse is released.
        /// New object is created and resized.
        /// </summary>
        public override void OnMouseUp(DrawingCanvas drawingCanvas, MouseButtonEventArgs e)
        {
            drawingCanvas.Tool = ToolType.Pointer;
            drawingCanvas.Cursor = HelperFunctions.DefaultCursor;
            drawingCanvas.ReleaseMouseCapture();

            if (drawingCanvas.Count > 0)
            {
                drawingCanvas[drawingCanvas.Count - 1].Graphic.Normalize();

                GraphicsText t = drawingCanvas[drawingCanvas.Count - 1].Graphic as GraphicsText;

                if (t != null)
                {
                    // Create textbox for editing of graphics object which is just created
                    CreateTextBox(drawingCanvas[drawingCanvas.Count - 1], drawingCanvas, true);
                }
            }

            // Commnnd will be added to History later, after closing
            // in-place textbox.
        }

        /// <summary>
        /// Create textbox for in-place editing
        /// </summary>
        public void CreateTextBox(GraphicsVisual graphics, DrawingCanvas drawingCanvas, bool newGraphic = false)
        {
            var graphicsText = graphics.Graphic as GraphicsText;
            if (graphicsText == null)
                return;

            graphicsText.IsSelected = false;  // selection marks don't look good with textbox

            // Keep old text in the case Esc is pressed while editing
            oldText = graphicsText.Body;

            // Keep reference to edited object
            editedGraphicsText = graphics;

            var gb = graphicsText.Bounds;

            _textBox = new TextBox();
            _textBox.RenderTransform = new RotateTransform(graphicsText.Angle);
            //textBox.Width = graphicsText.Bounds.Width;
            //textBox.Height = graphicsText.Bounds.Height;
            //textBox.FontFamily = new FontFamily(graphicsText.FontFamily);
            //textBox.FontSize = graphicsText.FontSize;
            //var tface = graphicsText.Typeface;
            //textBox.FontStretch = tface.Stretch;
            //textBox.FontStyle = tface.Style;
            //textBox.FontWeight = tface.Weight;
            //textBox.Text = graphicsText.Text;
            _textBox.Width = Double.NaN;
            _textBox.Height = Double.NaN;
            _textBox.Background = Brushes.Transparent;
            _textBox.FontFamily = new FontFamily("Arial");
            _textBox.Text = graphicsText.Body;
            _textBox.FontSize = 12;
            _textBox.BorderThickness = new Thickness(0, 0, 0, 1);
            _textBox.Tag = graphicsText;

            if (newGraphic)
            {
                _textBox.Text = graphicsText.Body = "start typing your note,\r\nuse shift+enter for new lines.";
                _textBox.SelectAll();
            }

            _textBox.AcceptsReturn = true;
            //_textBox.TextWrapping = TextWrapping.Wrap;

            drawingCanvas.Children.Add(_textBox);

            //Matrix matrix = new Matrix();
            //matrix.Rotate(graphicsText.Angle);
            //gb.Transform(matrix);

            Canvas.SetLeft(_textBox, gb.TopLeft.X + graphicsText.Padding);
            Canvas.SetTop(_textBox, gb.TopLeft.Y + graphicsText.Padding);

            _textBox.Focus();

            _textBox.LostFocus += new RoutedEventHandler(textBox_LostFocus);
            _textBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler(textBox_LostKeyboardFocus);
            _textBox.PreviewKeyDown += new KeyEventHandler(textBox_PreviewKeyDown);
            _textBox.ContextMenu = null;     // see notes in textBox_LostKeyboardFocus
            _textBox.TextChanged += textBox_TextChanged;

            // Initially textbox is set to the same rectangle as graphicsText.
            // After textbox loading its template is available, and we can
            // correct textbox position - see details in the textBox_Loaded function.
            _textBox.Loaded += new RoutedEventHandler(textBox_Loaded);
        }

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var grap = (sender as TextBox)?.Tag as GraphicsText;
            if (grap == null)
                return;
            grap.Body = ((TextBox)sender).Text;
        }


        /// <summary>
        /// Correct textbox position.
        /// Without this correction text shown in a textbox appears with some
        /// horizontal and vertical offset relatively to textbox bounding rectangle.
        /// We need to apply this correction, moving textbox in left-up direction.
        /// 
        /// Visually, text should not "jump" on the screen, when in-place editing
        /// textbox is open and closed.
        /// </summary>
        void textBox_Loaded(object sender, RoutedEventArgs e)
        {
            double xOffset, yOffset;

            ComputeTextOffset(_textBox, out xOffset, out yOffset);


            Canvas.SetLeft(_textBox, Canvas.GetLeft(_textBox) - xOffset);
            Canvas.SetTop(_textBox, Canvas.GetTop(_textBox) - yOffset);
            _textBox.Width = _textBox.Width + xOffset + xOffset;
            _textBox.Height = _textBox.Height + yOffset + yOffset;
        }


        /// <summary>
        /// Compute distance between textbox top-left point and actual
        /// text top-left point inside of textbox.
        /// 
        /// Thanks to Nick Holmes for showing this code in MSDN WPF Forum.
        /// </summary>
        static void ComputeTextOffset(TextBox tb, out double xOffset, out double yOffset)
        {
            // Set hard-coded values initially
            xOffset = 5;
            yOffset = 3;

            try
            {
                ContentControl cc = (ContentControl)tb.Template.FindName("PART_ContentHost", tb);

                /*
                // Way to see control template (Charles Petzold, Applications = Code + Markup, part 25).
                
                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = new string(' ', 4);
                settings.NewLineOnAttributes = true;

                System.Text.StringBuilder strbuild = new System.Text.StringBuilder();
                System.Xml.XmlWriter xmlwrite = System.Xml.XmlWriter.Create(strbuild, settings);
                System.Windows.Markup.XamlWriter.Save(tb.Template, xmlwrite);
                string s = strbuild.ToString();
                System.Diagnostics.Trace.WriteLine(s);
                 */

                GeneralTransform tf = ((Visual)cc.Content).TransformToAncestor(tb);
                Point offset = tf.Transform(new Point(0, 0));

                xOffset = offset.X;
                yOffset = offset.Y;
            }
            catch (ArgumentException e)
            {
                System.Diagnostics.Trace.WriteLine("ComputeTextOffset: " + e.Message);
            }
            catch (InvalidOperationException e)
            {
                System.Diagnostics.Trace.WriteLine("ComputeTextOffset: " + e.Message);
            }
        }


        // Hide textbox when Enter or Esc is changed
        void textBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _textBox.Text = oldText;

                drawingCanvas.HideTextbox(editedGraphicsText);

                e.Handled = true;
                return;
            }

            // Enter without modifiers - Shift+Enter should be available.
            if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.None)
            {
                drawingCanvas.HideTextbox(editedGraphicsText);

                e.Handled = true;
                return;
            }

            // Handle A-Z here, so that keybindings to those keys will not be handled later on.
            if ((int)e.Key >= 44 && (int)e.Key <= 69)
            {
                var key = (char)KeyInterop.VirtualKeyFromKey(e.Key);
                string str = "";
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    if (Console.CapsLock) str = key.ToString();
                    else str = key.ToString().ToLower();
                }
                else if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    if (Console.CapsLock) str = key.ToString().ToLower();
                    else str = key.ToString();
                }

                if (!String.IsNullOrWhiteSpace(str))
                {
                    var comp = new TextComposition(InputManager.Current, _textBox, str);
                    _textBox.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, comp)
                    {
                        RoutedEvent = TextCompositionManager.TextInputEvent
                    });
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Hide textbox when it looses focus.  
        /// </summary>
        void textBox_LostFocus(object sender, RoutedEventArgs e)
        {
            drawingCanvas.HideTextbox(editedGraphicsText);
        }


        /// <summary>
        /// Hide textbox when it looses keyboard focus.  
        /// </summary>
        void textBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            drawingCanvas.HideTextbox(editedGraphicsText);

            /// Notes:
            /// TextBox context menu is set to null in CreateTextBox function.
            /// The reason I did this is the following:
            /// I must hide textbox when user clicks anywhere
            /// outside of textbox, outside of this program window,
            /// or any other window pops up and steals focus.
            /// The only function which works for all these cases for 100%
            /// is LostKeyboardFocus handler. However, LostKeyboardFocus
            /// is raised also when textbox context menu is shown, and this
            /// breaks all logic. To keep things consistent, I don't allow
            /// showing context menu.
        }

        /// <summary>
        /// Textbox text value before in-place editing.
        /// </summary>
        public string OldText
        {
            get { return oldText; }
        }

    }
}
