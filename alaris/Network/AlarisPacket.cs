using System;
using System.Collections.Generic;

namespace Alaris.Network
{
    /// <summary>
    ///   Class used to create packets which will be sent between Alaris server and client.
    /// </summary>
    public class AlarisPacket : IDisposable
    {
        private string _netmsg;
        private readonly List<string> split_buffer;
        private const string Separator = "|;|";
        private int read_position;

        /// <summary>
        ///   Initializes a new instance of the <see cref = "AlarisPacket" /> class.
        /// </summary>
        /// <param name = 'net_message'>
        ///   Net message.
        /// </param>
        public AlarisPacket(string net_message)
        {
            _netmsg = net_message;

            split_buffer = new List<string>(_netmsg.Split((new[] {Separator}), StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref = "AlarisPacket" /> class.
        /// </summary>
        public AlarisPacket()
        {
            _netmsg = string.Empty;
        }

        /// <summary>
        ///   Read this instance.
        /// </summary>
        /// <typeparam name = 'T'>
        ///   The 1st type parameter.
        /// </typeparam>
        public T Read<T>()
        {
            if (read_position > (split_buffer.Count - 1))
                return ((T) Convert.ChangeType(0, typeof (T)));

            var ret = ((T) (Convert.ChangeType((split_buffer[read_position]), typeof (T))));
            ++read_position;

            if (typeof (T) == typeof (string))
            {
                return
                    ((T)
                     Convert.ChangeType((((string) (ret as object)).Replace("{[n]}", Environment.NewLine)), typeof (T)));
            }

            return ret;
        }

        /// <summary>
        ///   Write the specified object to the packet buffer.
        /// </summary>
        /// <param name = 'v'>
        ///   Object.
        /// </param>
        /// <typeparam name = 'T'>
        ///   Type of object.
        /// </typeparam>
        public void Write<T>(T v)
        {
            var append = ((string) Convert.ChangeType(v, typeof (string)));

            if (string.IsNullOrEmpty(append))
                return;

            _netmsg += (append + Separator);
        }

        /// <summary>
        ///   Resets the reader position.
        /// </summary>
        public void ResetReaderPosition()
        {
            read_position = 0;
        }

        /// <summary>
        ///   Gets the net message.
        /// </summary>
        /// <returns>
        ///   The net message.
        /// </returns>
        public string GetNetMessage()
        {
            return _netmsg;
        }

        /// <summary>
        ///   Gets the buffer.
        /// </summary>
        /// <returns>
        ///   The buffer.
        /// </returns>
        public List<string> GetBuffer()
        {
            return split_buffer;
        }


        /// <summary>
        ///   Releases all resource used by the <see cref = "AlarisPacket" /> object.
        /// </summary>
        /// <remarks>
        ///   Call <see cref = "Dispose" /> when you are finished using the <see cref = "AlarisPacket" />. The
        ///   <see cref = "Dispose" /> method leaves the <see cref = "AlarisPacket" /> in an unusable state. After calling
        ///   <see cref = "Dispose" />, you must release all references to the <see cref = "AlarisPacket" /> so the
        ///   garbage collector can reclaim the memory that the <see cref = "AlarisPacket" /> was occupying.
        /// </remarks>
        public void Dispose()
        {
            read_position = 0;
            split_buffer.Clear();
            _netmsg = string.Empty;

            GC.SuppressFinalize(this);
        }


        /// <summary>
        ///   Prepares the string.
        /// </summary>
        /// <returns>
        ///   The string.
        /// </returns>
        /// <param name = 'packetString'>
        ///   Packet string to prepare.
        /// </param>
        public static string PrepareString(string packetString)
        {
            if (packetString == null) throw new ArgumentNullException("packetString");
            return (packetString.Replace(Environment.NewLine, "{[n]}"));
        }
    }
}