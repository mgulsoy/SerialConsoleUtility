using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerialConsoleCore {
    public class BinaryEncoder : Encoder {
        public override int GetByteCount( char[] chars, int index, int count, bool flush ) {
            return 23;
        }

        public override int GetBytes( char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush ) {



            return 23;
        }

        public string GetString(Span<byte> bytes) {
            //This encoder uses ascii rules but also displays non-ascii chars

            StringBuilder sb = new StringBuilder(bytes.Length);

            foreach( var item in bytes ) {
                if( item > 127 ) {
                    //Non Ascii
                } else if( item < 32 ) {
                    //Control chars
                }
            }
            
            return "bla bla";
        }

    }

    public class BinaryEncoding : UTF8Encoding {

        //private static readonly char[] validHexChars = "0123456789ABCDEFabcdef".ToCharArray();

        private static BinaryEncoding _instance;

        public static BinaryEncoding Instance { 
            get {
                if( _instance == null )  _instance = new BinaryEncoding();

                return _instance;
            } 
        }

        /// <summary>
        /// Returns bytes of a string input according to the binary formatter. This method is based
        /// on UTF8Encoding.GetString(string)
        /// </summary>
        /// <param name="s">Input String</param>
        /// <returns>Bytes of the input string</returns>
        public override byte[] GetBytes( string s ) {
            byte[] bx =  base.GetBytes( s );

            List<byte> outputcoll = new List<byte>(s.Length);

            for( int i = 0; i < bx.Length; i++ ) {

                if( bx[i] == 35 ) { //if '#' this is escape
                                //expect format char x,d,o
                                // x -> hex
                                // d -> decimal
                                // o -> octal
                                // # ->  dash character

                    if( ++i < bx.Length ) {
                        //advance and check if there is one more char
                        if( bx[i] == '#' ) {
                            //escape dash
                            outputcoll.Add( bx[i] );
                            continue;
                        } else if( bx[i] == 'x' ) {
                            //Parse hex
                            if( i+2 < bx.Length ) {
                                //expect 2 bytes for hex
                                int hex = Math.Min((bx[++i] - 48) * 16, 0xF0);
                                hex += Math.Min( bx[++i] - 48, 0x0F );
                                byte hexByte = (byte)(hex & 0x000000FF);
                                outputcoll.Add( hexByte );                                                                                                
                            }
                            //If there is not enough bytes then do nothing
                            continue;
                        } else if( bx[i] == 'd' ) {
                            //parse decimal
                            if( i+3 < bx.Length ) {
                                //expect 3 bytes for decimal
                                int dec = Math.Min((bx[++i] - 48) * 100, 200 );
                                dec = Math.Min( dec + (bx[++i] - 48) * 10, 250 );
                                dec = Math.Min( dec + bx[++i] - 48, 255 );
                                byte hexByte = (byte)(dec & 0x000000FF);
                                outputcoll.Add( hexByte );
                            }
                            continue;
                        } else if( bx[i] == 'o' ) {
                            //Parse octal
                            if( i + 3 < bx.Length ) {
                                //expect 3 bytes for octal
                                int oct = Math.Min((bx[++i] - 48) * (8^2), 192 );
                                oct = Math.Min( oct + ( bx[++i] - 48 ) * 8, 248 );
                                oct = Math.Min( oct + bx[++i] - 48, 255 );
                                byte hexByte = (byte)(oct & 0x000000FF);
                                outputcoll.Add( hexByte );
                            }
                            continue;
                        }
                    }

                } else {
                    outputcoll.Add( bx[i] );
                }
            }

            return outputcoll.ToArray();

        }

        /// <summary>
        /// Returns bytes of a hex string seperated by space
        /// </summary>
        /// <param name="s">Hex string as input</param>
        /// <returns>Bytes of the input string</returns>
        public byte[] GetHexBytes(string s) {
            string[] hexElements = s.Split(" ");
            List<byte> outputcoll = new List<byte>(hexElements.Length);

            foreach( var hexElement in hexElements ) {
                int e_val = 0x000000FF;

                try {
                    e_val = System.Convert.ToInt32( hexElement, 16 );
                } catch( Exception ) { /* Do nothing in here */  }

                outputcoll.Add( (byte)(e_val & 0x000000FF) );
            }

            return outputcoll.ToArray();
        }
    }
}
