using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MessagePack;

namespace AIBuff {

    [MessagePackObject(true)]
    public class SaveData {
        public float HpMultiplier { get; set; } = Constants.DefaultHpMultiplier;
        public float DmgMultiplier { get; set; } = Constants.DefaultDmgMultiplier;
        public float ResourceMultiplier { get; set; } = Constants.DefaultResourceMultiplier;
    }
}
