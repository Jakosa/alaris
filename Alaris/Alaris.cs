using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Schema;
using Alaris.Administration;
using Alaris.API;
using Alaris.API.Database;
using Alaris.Commands;
using Alaris.Config;
using Alaris.Exceptions;
using Alaris.Irc;
using Alaris.Irc.Ctcp;
using Alaris.Threading;

namespace Alaris
{
    /// <summary>
    ///   The main class for Alaris.
    /// </summary>
    public partial class AlarisBot : IThreadContext, IDisposable
    {
        private Connection _connection;
        private ScriptManager _manager;
        private string _nick;
        private string _server;
        private bool _confdone;
        private bool _nickserv;
        private string _nspw = "";
        private readonly List<string> _channels = new List<string>();
        private readonly CrashHandler _sCrashHandler = Singleton<CrashHandler>.Instance;
        private readonly Guid _guid = Guid.NewGuid();
        private readonly string _configfile;
        private const int ListenerPort = 35221;
        private const string ACSHost = "127.0.0.1";
        private const int ACSPort = 35220;
        private string _scriptsDir;
        

        /// <summary>
        /// Database name.
        /// </summary>
        public string DBName { get; private set; }

        /// <summary>
        /// Gets whether the Lua engine is enabled or not.
        /// </summary>
        public bool LuaEnabled { get; private set; }

        /// <summary>
        /// Gets the remote's name.
        /// </summary>
        public string RemoteName { get; private set; }
        /// <summary>
        /// Gets the remote's port.
        /// </summary>
        public int RemotePort { get; private set; }

        /// <summary>
        /// Gets the remote's password.
        /// </summary>
        public string RemotePassword { get; private set; }

        /// <summary>
        ///   The bot's crash handler instance.
        /// </summary>
        public CrashHandler CrashHandler
        {
            get { return _sCrashHandler; }
        }

        /// <summary>
        /// Gets the current locale.
        /// </summary>
        public string Locale { get; private set; }

        /// <summary>
        ///   The bot's script manager instance.
        /// </summary>
        public ScriptManager ScriptManager
        {
            get { return _manager; }
        }

        /// <summary>
        /// Gets the list of channels the bot is on.
        /// </summary>
        public List<string> Channels
        {
            get { return _channels; }
        }

        /// <summary>
        /// Gets the IRC connection.
        /// </summary>
        public Connection Connection
        {
            get { return _connection; }
        }

        /// <summary>
        ///   Gets or sets the thread pool.
        /// </summary>
        /// <value>
        ///   The thread pool.
        /// </value>
        public CThreadPool Pool { get; private set; }

        /// <summary>
        ///   This is not an unused constructor. Called through singleton!
        /// </summary>
        private AlarisBot() : this("alaris.config.xml")
        {
        }


        /// <summary>
        ///   Creates a new instacne of Alaris bot.
        /// </summary>
        private AlarisBot(string config)
        {
            Log.Notice("Alaris", "Initalizing...");
            _configfile = config;
            CrashHandler.HandleReadConfig(ReadConfig, _configfile);
            var cargs = new ConnectionArgs(_nick, _server);
            Log.Debug("Identd", "Starting service...");
            /*Identd.Start(_nick);
            
            */
            Pool = new CThreadPool(4);

            try
            {

                Parallel.Invoke(() => Identd.Start(_nick),
                                () => { lock(Pool) {Pool = new CThreadPool(4);} },
                                () =>
                                    {


                                        _connection = new Connection(cargs, true, false)
                                                            {
                                                                TextEncoding = Encoding.GetEncoding("Latin1")
                                                            };

                                        var responder = new CtcpResponder(_connection)
                                                            {
                                                                VersionResponse = "Alaris " + Utilities.BotVersion,
                                                                SourceResponse = "http://www.wowemuf.org",
                                                                UserInfoResponse = "Alaris multi-functional bot."
                                                            };

                                        _connection.CtcpResponder = responder;

                                        Log.Success("CTCP", "Enabled.");

                                    },
                                () =>
                                    {
                                        _manager =
                                            new ScriptManager(
                                                ref _connection,
                                                _channels,
                                                _scriptsDir);
                                        
                                        lock(Pool){Pool.Enqueue(_manager);}
                                    });
            }
            catch(TargetInvocationException x)
            {
                Log.Error("Parallel", x.ToString());
                Log.LargeWarning("An exception has been thrown in a critical part of the program.");
            }

            //_connection = new Connection(cargs, true, false);

            CommandManager.CommandPrefix = "@";
            CommandManager.CreateMappings();

            //_connection.CtcpResponder = responder;
            Log.Notice("Alaris", "Text encoding: UTF-8");
            //_connection.TextEncoding = Encoding.GetEncoding("Latin1");

            //Log.Success("CTCP", "Enabled.");

            Log.Notice("ScriptManager", "Initalizing...");
            _manager = new ScriptManager(ref _connection, _channels, _scriptsDir);
            //_manager.LoadPlugins();
            //Thread.Sleep(2000);
            //Log.Success("ScriptManager", "Setup complete");

            Pool.Enqueue(_manager);

            DatabaseManager.Initialize(DBName);

            Log.Notice("Remoting", string.Format("Starting remoting channel on port {0} with name: {1}", RemotePort, RemoteName));
            RemoteManager.StartServives(RemotePort, RemoteName);

            SetupHandlers();
        }

        /// <summary>
        ///   Gets the listener port.
        /// </summary>
        /// <returns>
        ///   The listener port.
        /// </returns>
        public static int GetListenerPort()
        {
            return ListenerPort;
        }

        /// <summary>
        ///   Releases unmanaged resources and performs other cleanup operations before the <see cref = "AlarisBot" />
        ///   is reclaimed by garbage collection.
        /// </summary>
        ~AlarisBot()
        {
            Log.Debug("Alaris", "~AlarisBot()");
        }

        /// <summary>
        ///   Gets the GUID.
        /// </summary>
        public Guid GetGuid()
        {
            return _guid;
        }

        /// <summary>
        ///   Run this instance.
        /// </summary>
        public void Run()
        {

            Connect(); 
        }


        private void SetupHandlers()
        {
            Log.Notice("ScriptManager", "Setting up event handlers.");
            _manager.RegisterOnRegisteredHook(OnRegistered);
            _manager.RegisterOnPublicHook(OnPublicMessage);
            _connection.CtcpListener.OnCtcpRequest += OnCtcpRequest;
            Log.Success("ScriptManager", "Event handlers are properly setup.");
        }


        /// <summary>
        ///   Reads and parses the specified config file.
        /// </summary>
        /// <param name = "configfile">
        ///   The config file name.
        /// </param>
        /// <exception cref="ConfigFileInvalidException"></exception>
        private void ReadConfig(string configfile)
        {
            if (!File.Exists("./" + configfile))
                throw new FileNotFoundException(
                    "The config file specified could not be found. It is essential to have a configuration file in the directory of the bot. " +
                    configfile + " could not be found.");

            // read conf file.
            Log.Notice("Config", "Reading configuration file: " + configfile);

            var config = new XmlSettings(configfile, "alaris");

            config.Document.Schemas.Add("http://github.com/Twl/alaris", "Alaris.xsd");

            try
            {

                config.Document.Validate((sender, args) =>
                                             {
                                                 Log.Debug("XML", args.Message);

                                                 if (args.Exception != null)
                                                 {
                                                     Log.Error("Config", args.Exception.Message);
                                                 }

                                             });
            }
            catch(XmlSchemaValidationException x)
            {
                Log.Error("Config", "Config file is invalid!");
                throw new ConfigFileInvalidException(x.Message);
            }

            _server = config.GetSetting("config/irc/server", "irc.rizon.net");
            _nick = config.GetSetting("config/irc/nickname", "alaris");
            _nspw = config.GetSetting("config/irc/nickserv", "nothing");

            _nickserv = (_nspw != "nothing");

            var chans = config.GetSetting("config/irc/channels", "#skullbot,#hun_bot");
            var clist = chans.Split(',');

            foreach (var chan in clist.Where(Rfc2812Util.IsValidChannelName).AsParallel())
                _channels.Add(chan);

            Utilities.AdminNick = config.GetSetting("config/irc/admin/nick", "Twl");
            Utilities.AdminUser = config.GetSetting("config/irc/admin/user", "Twl");
            Utilities.AdminHost = config.GetSetting("config/irc/admin/host", "evil.from.behind");


            _scriptsDir = config.GetSetting("config/scripts/directory", "scripts");

            DBName = config.GetSetting("config/database", "Alaris").ToUpper(CultureInfo.InvariantCulture);

            LuaEnabled = config.GetSetting("config/scripts/LUA", "Disabled").Equals("Enabled");

            Locale = config.GetSetting("config/localization/locale", "enGB");

            Log.Debug("LocalizationManager", string.Format("Current locale is: {0}", Locale));

            RemotePort = Convert.ToInt32(config.GetSetting("config/remote/port", "5564"));
            RemoteName = config.GetSetting("config/remote/name", "RemoteManager");
            RemotePassword = config.GetSetting("config/remote/password", "alaris00");

            Log.Success("Config", "File read and validated successfully.");
            _confdone = true;

            
            Log.Notice("Config", string.Format("Connect to: {0} with nick {1}", _server, _nick));
        }


        /// <summary>
        ///   Establishes the connection to the previously specified server.
        /// </summary>
        private void Connect()
        {
            if (!_confdone)
                throw new Exception("The config file has not been read before connecting.");

            Log.Notice("Alaris", "Establishing connection...");
            try
            {
                _connection.Connect();
            }
            catch (Exception x)
            {
                Log.Error("Alaris", x.Message);
                Identd.Stop();
            }
        }

        /// <summary>
        ///   Disconnects the bot from the IRC server.
        /// </summary>
        /// <param name = "rsr">
        ///   Reason for disconnect.
        /// </param>
        public void Disconnect(string rsr)
        {
            Log.Notice("Alaris", "Disconnecting...");
            Pool.Free();

            if (Identd.IsRunning())
            {
                Identd.Stop();
                Log.Success("Identd", "Stopped service daemon");
            }

            //_manager.Lua.Free();

            try { _connection.Disconnect(rsr);}
            catch(InvalidOperationException)
            {
            }

            Environment.Exit(0);
        }

        /// <summary>
        ///   Method run when the bot is registered to the IRC server.
        /// </summary>
        private void OnRegistered()
        {
            // Stop Identd, no need for it anymore.
            Identd.Stop();
            Log.Success("Identd", "Stopped service daemon");
            Log.Success("Alaris", "Bot registered on server");

            // join channels here

            foreach (var chan in _channels)
            {
                if (Rfc2812Util.IsValidChannelName(chan))
                    _connection.Sender.Join(chan);

                Log.Notice("Alaris", "Joined channel: " + chan);
            }

        }

        /// <summary>
        ///   Method run when the bot receives a CTCP request.
        /// </summary>
        /// <param name = "command">
        ///   The CTCP command.
        /// </param>
        /// <param name = "user">
        ///   Data about the user who sent it.
        /// </param>
        private static void OnCtcpRequest(string command, UserInfo user)
        {
            Log.Notice("CTCP", "Received command " + command + " from " + user.Nick);
        }

        /// <summary>
        ///   Releases all used resources.
        /// </summary>
        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}