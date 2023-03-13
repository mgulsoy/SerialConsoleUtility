using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Win32;

/*
    Resources: https://stackoverflow.com/questions/14885288/i-o-exception-error-when-using-serialport-open
 */

namespace SerialConsoleCore {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        string stateFileName = "state.json";
        const string hexRegex = "#[0-9,a-f,A-F]{2}";

        SerialPort serialPort;
        Thread portWatcher;
        bool pollerEnabled = false;
        BinaryEncoder binaryEncoder = new BinaryEncoder();
        Task repeaterTask = null;
        CancellationTokenSource repeaterTokenSource = null;

        Encoding portEncoding;


        State programState;

        public static readonly RoutedCommand EditCommand = new RoutedCommand("Edit",typeof(RoutedCommand));
        public static readonly RoutedUICommand SendCommand = new RoutedUICommand("Send","Send",typeof(RoutedUICommand),
            new InputGestureCollection() {
                new KeyGesture(Key.F3)
            });
        public static readonly RoutedCommand RepeatCommand = new RoutedCommand("Repeat", typeof(RoutedCommand));
        public static readonly RoutedCommand StopRepeatCommand = new RoutedCommand("StopRepeat", typeof(RoutedCommand));
        public static readonly RoutedUICommand SendTboxCommand = new RoutedUICommand("SendTbox", "SendTbox", typeof(RoutedUICommand),
            new InputGestureCollection() {
                new KeyGesture(Key.Enter, ModifierKeys.Alt | ModifierKeys.Control )
            });

        public static readonly RoutedCommand ConvertToU8Command = new RoutedCommand("ConvertToU8", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToU16Command = new RoutedCommand("ConvertToU16", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToU32Command = new RoutedCommand("ConvertToU32", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToU64Command = new RoutedCommand("ConvertToU64", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToI8Command = new RoutedCommand("ConvertToI8", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToI16Command = new RoutedCommand("ConvertToI16", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToI32Command = new RoutedCommand("ConvertToI32", typeof(RoutedCommand));
        public static readonly RoutedCommand ConvertToI64Command = new RoutedCommand("ConvertToI64", typeof(RoutedCommand));

        public enum UtilEnum {
            Traffic, Chart
        }

        public UtilEnum SelectedUtil;

        public MainWindow() {
            InitializeComponent();

            msgBg.Visibility = Visibility.Hidden;
            msgCont.Visibility = Visibility.Hidden;

            fillPorts();

            loadState();

            //Set control data
            cmbBaud.SelectedIndex = programState.SelectedBaudIndex;
            cmbParity.SelectedIndex = programState.SelectedParityIndex;
            cmbEncoding.SelectedIndex = programState.SelectedEncodingIndex;
            rb7.IsChecked = programState.SelectedBitCount == 7;
            rb8.IsChecked = programState.SelectedBitCount == 8;

            Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );
            SelectedUtil = UtilEnum.Traffic;
        }

        private void loadState() {
            if( File.Exists( stateFileName ) ) {
                try {
                    string d = File.ReadAllText(stateFileName);
                    programState = JsonSerializer.Deserialize<State>( d );
                } catch( Exception ) { }
            }
            if( programState == null ) {
                programState = new State();
            }

            //LastDataToSend collection
            if( programState.LastDataToSendColl == null ) {
                programState.LastDataToSendColl = new List<DataToSend>();
            }

            lstSendData.Items.Clear();
            foreach( var item in programState.LastDataToSendColl ) lstSendData.Items.Add( item );

            txtCompose.Text = programState.LastComposeBoxContent;

        }

        private void saveState() {

            programState.SelectedBitCount = rb7.IsChecked.Value ? 7 : 8;
            programState.SelectedBaudIndex = cmbBaud.SelectedIndex;
            programState.SelectedParityIndex = cmbParity.SelectedIndex;
            programState.SelectedEncodingIndex = cmbEncoding.SelectedIndex;

            programState.LastDataToSendColl.Clear();
            programState.LastDataToSendColl.AddRange( lstSendData.Items.SourceCollection.Cast<DataToSend>() );

            programState.LastComposeBoxContent = txtCompose.Text;

            File.WriteAllText( stateFileName, JsonSerializer.Serialize( programState ) );
        }

        private void showMsgbox( string msg ) {
            tbMsg.Text = msg;

            msgBg.Visibility = Visibility.Visible;
            msgCont.Visibility = Visibility.Visible;
        }

        private void hideMsgbox() {
            msgBg.Visibility = Visibility.Hidden;
            msgCont.Visibility = Visibility.Hidden;
            hlpCompose.Visibility = Visibility.Hidden;
        }

        private void fillPorts() {
            cmbPorts.Items.Clear();
            foreach( var item in SerialPort.GetPortNames() ) {
                cmbPorts.Items.Add( item );
            }
        }

        private void AppendLog(string logMessage) {
            Dispatcher.InvokeAsync( () => {
                Run run = new Run(Environment.NewLine + logMessage + Environment.NewLine); ;
                run.Foreground = Brushes.Crimson;
                run.FontFamily = new FontFamily( "Segoe UI" );
                run.FontWeight = FontWeights.Bold;
                content.Inlines.Add( run );

                rdScroller.ScrollToEnd();
            } );
        }

        private void AppendValue(string logMessage) {
            Dispatcher.InvokeAsync(() => {
                Run run = new Run(Environment.NewLine + logMessage + Environment.NewLine); ;
                run.Foreground = Brushes.BlueViolet;
                run.FontFamily = new FontFamily("Consolas");
                run.FontWeight = FontWeights.Regular;
                content.Inlines.Add(run);

                rdScroller.ScrollToEnd();
            });
        }

        private void AppendReceived( byte[] data ) {
 
            Dispatcher.InvokeAsync( () => {

                bool showHex = chkShowHex.IsChecked ?? false;
                string text = "";
                if( showHex ) {
                    StringBuilder sb = new StringBuilder();
                    for( int i = 0; i < data.Length; i++ ) {
                        sb.AppendFormat( "{0,3:x2}", data[i] );
                    }

                    text = sb.ToString();
                } else {
                    text = portEncoding.GetString( data );
                }


                Run run = new Run(text); ;
                run.Foreground = showHex ?  Brushes.CornflowerBlue : Brushes.Green;
                if( showHex ) run.FontWeight = FontWeights.Bold;
                
                run.Tag = Brushes.LightGreen;
                run.MouseEnter += Run_MouseEnter;
                run.MouseLeave += Run_MouseLeave;
                content.Inlines.Add( run );

                rdScroller.ScrollToEnd();
            } );
        }

        private void AppendChart( byte[] lbuf ) {
            
        }

        private void AppendSent( string s ) {
            Dispatcher.InvokeAsync( () => {
                bool showHex = chkShowHex.IsChecked ?? false;

                string ss = s;

                if( showHex ) {
                    ss = Environment.NewLine + s + Environment.NewLine;
                }

                Run run = new Run(ss); ;
                run.Foreground = Brushes.DeepPink;
                run.Tag = Brushes.LightPink;
                run.MouseEnter += Run_MouseEnter;
                run.MouseLeave += Run_MouseLeave;
                content.Inlines.Add( run );

                rdScroller.ScrollToEnd();
            } );
        }

        private void Run_MouseLeave( object sender, MouseEventArgs e ) {
            Run r = sender as Run;
            r.Background = Brushes.White;
        }

        private void Run_MouseEnter( object sender, MouseEventArgs e ) {
            Run r = sender as Run;
            r.Background = r.Tag as Brush;
        }

        private void setControlState( bool enabled ) {
            cmbPorts.IsEnabled = enabled;
            cmbBaud.IsEnabled = enabled;
            cmbParity.IsEnabled = enabled;
            cmbEncoding.IsEnabled = enabled;
            rddBits.IsEnabled = enabled;
        }

        private void portWatcherWork() {
            //This method watches the serial port and receives the data.
            try {
                while( pollerEnabled ) {
                    int available = serialPort.BytesToRead;
                    if( available > 0 ) {
                        byte[] lbuf = new byte[available];
                        serialPort.Read( lbuf, 0, available );
                        if( SelectedUtil == UtilEnum.Traffic ) {
                            AppendReceived( lbuf );
                        } else if( SelectedUtil == UtilEnum.Chart ) {
                            // A circular buffer can be utilized to operate
                            AppendChart( lbuf );
                        }
                                                
                    }
                    Dispatcher.InvokeAsync( new Action( () => {
                        if( serialPort != null ) {
                            rdCTS.IsChecked = serialPort.CtsHolding;
                            rdDSR.IsChecked = serialPort.DsrHolding;
                        }
                    } ) );
                    Thread.Sleep( 30 );
                }
            } catch( Exception ex ) {
                //Todo: handle port exceptions
                Debug.Print( ex.Message );
                return;
            }
        }

        private void send( DataToSend data ) {

            if( !(serialPort != null && serialPort.IsOpen) ) {
                //If there is no connection warn the user
                showMsgbox( "Please connect first" );
                return;
            }

            

            DataToSend dts = new DataToSend();
            dts.IsHex = data.IsHex;

            if( rdElCr.IsChecked.Value ) {
                //treat end line as CR
                dts.DataString = data.DataString.Replace( "\r\n", "\r" );
            } else if( rdElLf.IsChecked.Value ) {
                //treat end line as CR
                dts.DataString = data.DataString.Replace( "\r\n", "\n" );
            } else {
                dts = data;
            }

            byte[] dt = dts.DataBytes;
            AppendSent( data.DataString );        

            serialPort.Write( dt, 0, dt.Length );
        }

        private void OpenConnection() {
            //control variables
            if( cmbPorts.SelectedItem == null || string.IsNullOrEmpty( cmbPorts.SelectedValue.ToString() ) ) {
                showMsgbox( "Please select a port to open!" );
                tbtnConnect.IsChecked = false;
                return;
            }

            //Open port
            if( serialPort == null ) {
                Parity p = Parity.None;
                if( cmbParity.SelectedValue.ToString() == "Even" ) {
                    p = Parity.Even;
                } else if( cmbParity.SelectedValue.ToString() == "Odd" ) {
                    p = Parity.Odd;
                }

                int dbit = 8;
                if( rb7.IsChecked.HasValue && rb7.IsChecked.Value ) {
                    dbit = 7;
                }

                serialPort = new SerialPort(
                    cmbPorts.SelectedValue.ToString(),
                    int.Parse( ( (ComboBoxItem)cmbBaud.SelectedItem ).Content.ToString() ),
                    p,
                    dbit
                );

                serialPort.ReadTimeout = 1000;

                string encodingSel = ((ComboBoxItem)cmbEncoding.SelectedItem).Content.ToString();


                if( encodingSel == "Utf-8" ) {
                    portEncoding = Encoding.UTF8;
                } else if( encodingSel == "Cp-1254" ) {
                    portEncoding = Encoding.GetEncoding( 1254 );
                } else if( encodingSel == "Cp-1252" ) {
                    portEncoding = Encoding.GetEncoding( 1252 );
                } else if( encodingSel == "ASCII" ) {
                    portEncoding = Encoding.ASCII;
                }

                try {
                    serialPort.Open();
                    portWatcher = new Thread( new ThreadStart( portWatcherWork ) );
                    pollerEnabled = true;
                    portWatcher.Start();
                    AppendLog( $"{serialPort.PortName} opened at {serialPort.BaudRate} baud." );
                } catch( Exception ex ) {
                    showMsgbox( ex.Message );
                    serialPort = null;

                    //change button content
                    tbtnConnect.Content = "Open";
                    setControlState( true );
                    return;
                }
            } else {
                showMsgbox( "The port is open! Close first!" );
            }


            //change button content
            tbtnConnect.Content = "Close";
            setControlState( false );
        }

        private void CloseConnection() {
            if( serialPort != null )
                if( serialPort.IsOpen ) {
                    serialPort.Close();

                    string name = serialPort.PortName;

                    //shutdown thread
                    if( portWatcher != null ) {
                        //portWatcher.Abort();
                        pollerEnabled = false;
                        portWatcher.Join();
                    }
                    serialPort = null;

                    //change button content
                    tbtnConnect.Content = "Open";
                    setControlState( true );
                    AppendLog( $"{name} closed" );
                }

        }

        private void tbtnConnect_Click( object sender, RoutedEventArgs e ) {
            if( tbtnConnect.IsChecked.HasValue && tbtnConnect.IsChecked.Value ) {
                OpenConnection();
            } else {
                CloseConnection();
            }
        }

        private void Button_Click( object sender, RoutedEventArgs e ) {
            //Close the messagebox
            hideMsgbox();
        }

        private void Window_Closing( object sender, System.ComponentModel.CancelEventArgs e ) {
            CloseConnection();
            saveState();
        }

        private void Border_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e ) {
            if( e.LeftButton == MouseButtonState.Pressed ) {
                var dataString = (sender as Border).Tag as string;
                //Debug.Print( "Seçilen eleman {0}", item );

            }
        }

        private void chkDTR_Click( object sender, RoutedEventArgs e ) {
            if( serialPort != null && serialPort.IsOpen ) {
                serialPort.DtrEnable = chkDTR.IsChecked.Value;
            }
        }

        private void chkRTS_Click( object sender, RoutedEventArgs e ) {
            if( serialPort != null && serialPort.IsOpen ) {
                serialPort.RtsEnable = chkRTS.IsChecked.Value;
            }
        }

        private void btnAddData_Click( object sender, RoutedEventArgs e ) {
            //Save the compose text box contents
            if( !string.IsNullOrEmpty( txtCompose.Text ) ) {

                msgBg.Visibility = Visibility.Visible;
                inpCont.Visibility = Visibility.Visible;
            }
        }

        private void EditCommandBinding_CanExecute( object sender, CanExecuteRoutedEventArgs e ) {
            e.CanExecute = string.IsNullOrEmpty( txtCompose.Text ) && ( lstSendData.SelectedItem != null );
        }

        private void EditCommandBinding_Executed( object sender, ExecutedRoutedEventArgs e ) {
            //Edit command is executed on listViewItem
            //So to reach data, we follow this route:
            var src = e.OriginalSource as ListViewItem;
            if( src != null ) {
                DataToSend dataToSend = src.Content as DataToSend;
                //Todo: Handle hex
                txtCompose.Text = dataToSend.DataString;
            }
        }

        private void DeleteCommandBinding_CanExecute( object sender, CanExecuteRoutedEventArgs e ) {
            e.CanExecute = lstSendData.SelectedItem != null;
        }

        private void DeleteCommandBinding_Executed( object sender, ExecutedRoutedEventArgs e ) {
            lstSendData.Items.Remove( lstSendData.SelectedItem );
        }

        private void SendCommandBinding_CanExecute( object sender, CanExecuteRoutedEventArgs e ) {
            e.CanExecute = lstSendData.SelectedItem != null
                && serialPort != null
                && serialPort.IsOpen;
        }

        private void SendCommandBinding_Executed( object sender, ExecutedRoutedEventArgs e ) {
            //This only sends the selected list data
            send( lstSendData.SelectedItem as DataToSend );
        }

        private void SendTboxCommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = serialPort != null && serialPort.IsOpen;
        }

        private void SendTboxCommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            btnSend_Click(null, null);
        }

        private void RepeatCommandBinding_CanExecute( object sender, CanExecuteRoutedEventArgs e ) {
            e.CanExecute = lstSendData.SelectedItem != null
                && repeaterTask == null 
                && serialPort != null
                && serialPort.IsOpen;
        }

        private void RepeatCommandBinding_Executed( object sender, ExecutedRoutedEventArgs e ) {
            //This only repeats on selected list item            
            msgBg.Visibility = Visibility.Visible;
            repeatDialog.Visibility = Visibility.Visible;
        }

        private void ConvertToU8CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            // Selected lenght must be gt 1
            e.CanExecute = rdViewer.Selection.Text.Length > 0 && rdViewer.Selection.Text.Length < 3;
        }

        private void ConvertToU16CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            e.CanExecute = t.Length > 2 && t.Length < 5;
        }

        private void ConvertToU32CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            e.CanExecute = t.Length > 4 && t.Length < 9;
        }

        private void ConvertToU64CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            e.CanExecute = t.Length > 8 && t.Length < 17;
        }
        
        private void ConvertToI8CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            // Selected lenght must be gt 1
            e.CanExecute = rdViewer.Selection.Text.Length > 0 && rdViewer.Selection.Text.Length < 3;
        }

        private void ConvertToI16CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            e.CanExecute = t.Length > 2 && t.Length < 5;
        }

        private void ConvertToI32CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            e.CanExecute = t.Length > 4 && t.Length < 9;
        }

        private void ConvertToI64CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            e.CanExecute = t.Length > 8 && t.Length < 17;
        }

        private void ConvertToU8CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            var val = Convert.ToByte(t, 16);
            AppendValue("Uint8: "+ t+ " -> " + val);
        }

        private void ConvertToU16CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "").PadLeft(4,'0');
            var val = Convert.ToUInt16(t,16);
            var valBytes = BitConverter.GetBytes(val);
            var lEndianVal = BitConverter.ToUInt16(valBytes.Reverse().ToArray());
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("UInt16  [BE:{0}]:{1}  [LE:", t,val);
            foreach (var i in valBytes) {
                sb.AppendFormat("{0:x2}",i);
            }
            sb.AppendFormat("]:{0}", lEndianVal);
            AppendValue(sb.ToString());
        }

        private void ConvertToU32CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "").PadLeft(8,'0');
            var val = Convert.ToUInt32(t, 16);
            var valBytes = BitConverter.GetBytes(val);
            var lEndianVal = BitConverter.ToUInt32(valBytes.Reverse().ToArray());
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("UInt32  [BE:{0}]:{1}  [LE:", t, val);
            foreach (var i in valBytes) {
                sb.AppendFormat("{0:x2}", i);
            }
            sb.AppendFormat("]:{0}", lEndianVal);
            AppendValue(sb.ToString());
        }

        private void ConvertToU64CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "").PadLeft(16,'0');
            var val = Convert.ToUInt64(t, 16);
            var valBytes = BitConverter.GetBytes(val);
            var lEndianVal = BitConverter.ToUInt64(valBytes.Reverse().ToArray());
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("UInt64  [BE:{0}]:{1}  [LE:", t, val);
            foreach (var i in valBytes) {
                sb.AppendFormat("{0:x2}", i);
            }
            sb.AppendFormat("]:{0}", lEndianVal);
            AppendValue(sb.ToString());
        }


        private void ConvertToI8CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "");
            var val = Convert.ToByte(t,16);
            AppendValue("Int8:" + val);
        }


        private void ConvertToI16CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "").PadLeft(4,'0');
            var val = Convert.ToInt16(t, 16);
            var valBytes = BitConverter.GetBytes(val);
            var lEndianVal = BitConverter.ToInt16(valBytes.Reverse().ToArray());
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Int16  [BE:{0}]:{1}  [LE:", t, val);
            foreach (var i in valBytes) {
                sb.AppendFormat("{0:x2}", i);
            }
            sb.AppendFormat("]:{0}", lEndianVal);
            AppendValue(sb.ToString());
        }

        

        private void ConvertToI32CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "").PadLeft(8, '0');
            var val = Convert.ToInt32(t, 16);
            var valBytes = BitConverter.GetBytes(val);
            var lEndianVal = BitConverter.ToInt32(valBytes.Reverse().ToArray());
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Int32  [BE:{0}]:{1}  [LE:", t, val);
            foreach (var i in valBytes) {
                sb.AppendFormat("{0:x2}", i);
            }
            sb.AppendFormat("]:{0}", lEndianVal);
            AppendValue(sb.ToString());
        }
        
        private void ConvertToI64CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) {
            var t = rdViewer.Selection.Text.Replace(" ", "").PadLeft(16, '0');
            var val = Convert.ToInt64(t, 16);
            var valBytes = BitConverter.GetBytes(val);
            var lEndianVal = BitConverter.ToInt32(valBytes.Reverse().ToArray());
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Int64  [BE:{0}]:{1}  [LE:", t, val);
            foreach (var i in valBytes) {
                sb.AppendFormat("{0:x2}", i);
            }
            sb.AppendFormat("]:{0}", lEndianVal);
            AppendValue(sb.ToString());
        }

        private void btnSend_Click( object sender, RoutedEventArgs e ) {
            //Sends the data inside txtCommpose
            //sendDataString( txtCompose.Text );
            if( serialPort != null && serialPort.IsOpen ) {
                send( new DataToSend( txtCompose.Text ) );
            } else {
                showMsgbox( "Please open a connection first" );
            }
            
        }

        private void CancelRepeat_Click( object sender, RoutedEventArgs e ) {
            //Cancels repeat op.
            repeatDialog.Visibility = Visibility.Hidden;
            msgBg.Visibility = Visibility.Hidden;
        }

        private void StartRepeat_Click( object sender, RoutedEventArgs e ) {
            repeatDialog.Visibility = Visibility.Hidden;
            msgBg.Visibility = Visibility.Hidden;

            repeaterTokenSource = new CancellationTokenSource();
            DataToSend dts = lstSendData.SelectedItem as DataToSend;
            if( !int.TryParse( txtRepeatInterval.Text, out int sleepTimeInterval ) ) {
                sleepTimeInterval = 1000; //if conversion fails then accept 1000 ms
                txtRepeatInterval.Text = sleepTimeInterval.ToString();
            }

            if( !int.TryParse( txtRepeatCount.Text, out int repeatCount ) ) {
                repeatCount = 1000; //if conversion fails then accept 1000 ms
                txtRepeatCount.Text = repeatCount.ToString();
            }

            repeaterTask = Task.Run(
                new Action( () => {
                    int rc = repeatCount;
                    int sti = sleepTimeInterval;
                    while( !repeaterTokenSource.Token.IsCancellationRequested && rc > 0 ) {
                        Dispatcher.InvokeAsync( () => send( dts ) );
                        Thread.Sleep( sti );
                        rc--;
                    }
                    repeaterTask = null; //Remove yourself to be gc'd.
                } ),
                repeaterTokenSource.Token
            );
        }

        private void StopRepeatCommandBinding_CanExecute( object sender, CanExecuteRoutedEventArgs e ) {
            e.CanExecute = repeaterTask != null && !repeaterTokenSource.IsCancellationRequested;
        }

        private void StopRepeatCommandBinding_Executed( object sender, ExecutedRoutedEventArgs e ) {
            repeaterTokenSource.Cancel();
        }

        private void cmbPorts_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e ) {
            fillPorts();
        }

        private void lstSendData_MouseDoubleClick( object sender, MouseButtonEventArgs e ) {
            //Debug.Print( "Double click on: {0}", lstSendData.SelectedItem );
            send( lstSendData.SelectedItem as DataToSend );
        }

        /// <summary>
        /// Produces a reset signal on DTR pin. For arduino like boards.
        /// </summary>
        private async void btnResetArduino_Click( object sender, RoutedEventArgs e ) {
            //Create a reset signal for a connected arduino
            if( serialPort != null && serialPort.IsOpen ) {
                btnResetArduino.IsEnabled = false;
                await Task.Run( new Action( () => {
                    serialPort.DtrEnable = true;
                    Thread.Sleep( 500 );
                    serialPort.DtrEnable = false;
                } ) );
                btnResetArduino.IsEnabled = true;
            } else {
                showMsgbox( "Please open a connection first" );
            }
        }

        /// <summary>
        /// Clears the traffic data view.
        /// </summary>
        /// <param name="sender">The button</param>
        /// <param name="e">Event Parameters</param>
        private void btnClearTraffic_Click( object sender, RoutedEventArgs e ) {
            content.Inlines.Clear();
        }

        private void btnSaveProceed_Click( object sender, RoutedEventArgs e ) {
            // Proceed and save
            // Ask for a label


            DataToSend dataToSend = new DataToSend() {
                DataString = txtCompose.Text,
                IsHex = rdHex.IsChecked.Value,
                Label = tbInp.Text,
            };

            lstSendData.Items.Add( dataToSend );

            msgBg.Visibility = inpCont.Visibility = Visibility.Hidden;
        }

        private void btnComposeHelp_Click( object sender, RoutedEventArgs e ) {
            // Show help for compose
            msgBg.Visibility = Visibility.Visible;
            hlpCompose.Visibility = Visibility.Visible;
        }

        private void TabItem_GotFocus( object sender, RoutedEventArgs e ) {
            Debug.Print( "Traffic got focus" );
            SelectedUtil = UtilEnum.Traffic;
        }

        private void TabItem_GotFocus_1( object sender, RoutedEventArgs e ) {
            Debug.Print( "Chart Got focus" );
            SelectedUtil = UtilEnum.Chart;
        }

        private void btnLoadProfile_Click(object sender, RoutedEventArgs e) {
            //Loads a custom profile file

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = false;
            ofd.Title = "Select a profile file";
            ofd.Filter = "Profile File|*.json";

            var result = ofd.ShowDialog();

            if (result.HasValue && result.Value) {
                // Open requested
                // save previous state first
                saveState();
                stateFileName = ofd.FileName;
                // load new one
                loadState();
            }
        }
    }
}
