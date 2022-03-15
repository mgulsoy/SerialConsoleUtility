using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerialConsoleCore {
    public class State {

        public int SelectedBaudIndex { get; set; }

        public int SelectedBitCount { get; set; }

        public int SelectedParityIndex { get; set; }

        public int SelectedEncodingIndex { get; set; }

        public List<DataToSend> LastDataToSendColl { get; set; }

        public string LastComposeBoxContent { get; set; }
        
    }
}
