using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityWebrtc.Marshalling
{
    public class FramePacket
    {
        public FramePacket(int bufsize)
        {
            _buffer = new byte[bufsize];
        }

        public int width;
        public int height;
        private byte[] _buffer;
        public byte[] Buffer
        {
            get { return _buffer; }
        }

        public override string ToString()
        {
            return "FramePacket width, height=(" + width + "," + height + ") buffer size:" + _buffer.Length;
        }
    }
}
