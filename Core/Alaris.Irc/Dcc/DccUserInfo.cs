using System.Net;

namespace Alaris.Irc.Dcc
{
	/// <summary>
	/// This class encapsulates all the information known
	/// about a remote user in the context of a DCC session.
	/// </summary>
	public sealed class DccUserInfo : UserInfo
	{

		private readonly Connection _connection;

		internal readonly IPEndPoint remoteEndPoint;

		/// <summary>
		/// Create a new instance.
		/// </summary>
		/// <param name="connection">The originating connection instance.</param>
		/// <param name="userInfoParts">The parsed nick!user@host string</param>
		/// <param name="remoteEndPoint">The TCP/IP settings from the other user.</param>
		internal DccUserInfo( Connection connection, string[] userInfoParts , IPEndPoint remoteEndPoint ) :
			base( userInfoParts[0],userInfoParts[1],userInfoParts[2])
		{
			_connection = connection;
			this.remoteEndPoint = remoteEndPoint;
		}

		internal DccUserInfo( Connection connection, string[] userInfoParts) :
			base( userInfoParts[0],userInfoParts[1],userInfoParts[2])
		{
			_connection = connection;
		}
		/// <summary>
		/// Create an instance that represents a user.
		/// </summary>
		/// <param name="connection">The IRC server connection which the remote user
		/// is on.</param>
		/// <param name="nick">The remote user's nick.</param>
		public DccUserInfo( Connection connection, string nick) :
			base( nick,"","")
		{
			_connection = connection;
		}

		/// <summary>
		/// Read only property that returns the
		/// IP address of the remote user.
		/// </summary>
		/// <value>An instance of IPAddress or null if the session
		/// has not been opened.</value>
		public IPAddress RemoteAddress
		{
			get
			{
				if( remoteEndPoint == null ) 
				{
					return null;
				}
				return remoteEndPoint.Address;
			}
		}
		/// <summary>
		/// Read only property that returns the port
		/// of the connection to the remote user. 
		/// </summary>
		/// <remarks>
		/// This will be a listen port if the remote user was
		/// the initiator of the session or else it is simply a client port.
		/// </remarks>
		/// <value>The port as an integer. Will return -1 if the session 
		/// has not yet been opened.</value>
		public int Port
		{
			get
			{
				if( remoteEndPoint == null ) 
				{
					return -1;
				}
				return remoteEndPoint.Port;
			}
		}
		/// <summary>
		/// The remote users TCP/IP information.
		/// </summary>
		/// <value>A read-only instance of IPEndPoint</value>
		public IPEndPoint RemoteEndPoint 
		{
			get 
			{
				return remoteEndPoint;
			}
		}
		/// <summary>
		/// The connection representing on which IRC server
		/// the remote user can be found.
		/// </summary>
		/// <value>A read-only instance of Connection</value>
		public Connection Connection 
		{
			get 
			{
				return _connection;
			}
		}

		/// <summary>
		/// A friendly representation of this object.
		/// </summary>
		/// <returns>The remote's user nick and his IP address, e.g. Nick@192.168.0.23</returns>
		public override string ToString() 
		{
			if( RemoteAddress == null ) 
			{
				return Nick;
			}
			else 
			{
				return Nick + "@" + RemoteAddress.ToString();
			}
		}
	}
}
