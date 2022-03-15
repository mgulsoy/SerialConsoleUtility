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

/*
    Resources: https://stackoverflow.com/questions/14885288/i-o-exception-error-when-using-serialport-open
 */

namespace SerialConsoleCore {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        const string stateFileName = "state.json";
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

        private void AppendReceived( byte[] data ) {
 
            Dispatcher.InvokeAsync( () => {

                string text = portEncoding.GetString(data);

                Run run = new Run(text); ;
                run.Foreground = Brushes.Green;
                run.Tag = Brushes.LightGreen;
                run.MouseEnter += Run_MouseEnter;
                run.MouseLeave += Run_MouseLeave;
                content.Inlines.Add( run );

                rdScroller.ScrollToEnd();
            } );
        }

        private void AppendSent( string s ) {
            Dispatcher.InvokeAsync( () => {
                Run run = new Run(s); ;
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
                        AppendReceived( lbuf );                        
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
    }
}
