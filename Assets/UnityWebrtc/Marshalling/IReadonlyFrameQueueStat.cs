using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityWebrtc.Marshalling
{
    /// <summary>
    /// A readonly frame queue stat
    /// </summary>
    public interface IReadonlyFrameQueueStat
    {
        // <summary>
        /// The average value calculated for the stat
        /// </summary>
        float Value
        {
            get;
        }
    }
}
