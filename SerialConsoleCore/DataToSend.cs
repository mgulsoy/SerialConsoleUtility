using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerialConsoleCore {
    public class DataToSend {
        public string DataString { get; set; }

        public bool IsHex { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public byte[] DataBytes { 
            get {
                if( IsHex ) {
                    return BinaryEncoding.Instance.GetHexBytes( DataString );
                } else 
                    return BinaryEncoding.Instance.GetBytes( DataString );
            } 
        }

        public DataToSend() {  }

        public DataToSend(string dataString) {
            DataString = dataString;
        }

    }
}
