﻿#region Header

// /* Copyright (C) 2013 Bence Horváth <horvathb@me.com>
//      alaris: Alaris.Framework: Utility.cs
// 
//      Last updated: 2013/04/19 8:22 PM
// 
// */

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Alaris.Framework.CommandLine;
using Alaris.Irc;
using NLog;

// Author: The WCell Team

namespace Alaris.Framework
{
    /// <summary>
    ///     Contains miscellaneous utility method used throughout the project.
    /// </summary>
    /// <remarks>
    ///     Things that can't be added as extension methods, or are too miscellaneous
    ///     will most likely be in this class.
    /// </remarks>
    public static class Utility
    {
        private static readonly DateTime UnixTimeStart = new DateTime(1970, 1, 1, 0, 0, 0);

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly Object SendLock = new Object();

        private static readonly Dictionary<string, Type> TypeMap =
            new Dictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);

        private static readonly Dictionary<Type, Dictionary<string, object>> EnumValueMap =
            new Dictionary<Type, Dictionary<string, object>>(300);

        static Utility()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                InitEnums(asm);
            }

            AppDomain.CurrentDomain.AssemblyLoad += CurrentDomain_AssemblyLoad;
            // init all operators

            // "|" is an escape character for the WoW client, they always get doubled
            IntOperators["||"] = BinaryOrHandler;

            IntOperators["|"] = BinaryOrHandler;
            IntOperators["^"] = BinaryXOrHandler;
            IntOperators["&"] = BinaryAndHandler;
            IntOperators["+"] = PlusHandler;
            IntOperators["-"] = MinusHandler;
            IntOperators["*"] = DivideHandler;
            IntOperators["/"] = MultiHandler;

            TypeMap.Add("UInt32", typeof (uint));
            TypeMap.Add("UInt64", typeof (ulong));
            TypeMap.Add("Int32", typeof (int));
            TypeMap.Add("Int64", typeof (long));

            /*Admins.Add(new Admin
                {
                    User = Operator.User,
                    Nick = Operator.Nick,
                    Host = Operator.Host
                });*/
        }

        /// <summary>
        ///     Gets the bot version.
        /// </summary>
        /// <value>
        ///     The bot version.
        /// </value>
        public static string BotVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }

        private static void CurrentDomain_AssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            InitEnums(args.LoadedAssembly);
        }

        private static void InitEnums(Assembly asm)
        {
            AddTypesToTypeMap(asm);
        }


        /// <summary>
        ///     Adds all non-standard Enum-types of the given Assembly to the TypeMap.
        ///     Also caches all big enums into a dictionary to improve Lookup speed.
        /// </summary>
        /// <param name="asm"></param>
        public static void AddTypesToTypeMap(Assembly asm)
        {
            if (asm.FullName == null)
            {
                return;
            }
            if (!asm.FullName.StartsWith("System.") && !asm.FullName.StartsWith("Microsoft.")
                && !asm.FullName.StartsWith("NHibernate")
                && !asm.FullName.StartsWith("Castle")
                && !asm.FullName.StartsWith("msvc")
                && !asm.FullName.StartsWith("NLog")
                && !asm.FullName.StartsWith("mscorlib"))
            {
                foreach (var type in asm.GetTypes())
                {
                    if (!type.FullName.StartsWith("System.") && !type.FullName.StartsWith("Microsoft."))
                    {
                        if (type.IsValueType)
                        {
                            TypeMap[type.FullName] = type;
                            if (type.IsEnum)
                            {
                                var values = Enum.GetValues(type);
                                //if (values.Length >= 100)
                                {
                                    var dict = new Dictionary<string, object>(values.Length + 100,
                                                                              StringComparer.InvariantCultureIgnoreCase);
                                    var names = Enum.GetNames(type);
                                    for (var i = 0; i < names.Length; i++)
                                    {
                                        dict[names[i]] = values.GetValue(i);
                                    }
                                    EnumValueMap[type] = dict;
                                }
                            }
                        }
                    }
                }
            }
        }

        #region Times

        private const int TicksPerSecond = 10000;
        private const long TicksSince1970 = 621355968000000000; // .NET ticks for 1970

        /// <summary>
        ///     Converts DateTime to miliseconds.
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public static int ToMilliSecondsInt(this DateTime time)
        {
            return (int) (time.Ticks/TicksPerSecond);
        }

        /// <summary>
        ///     Converts TimeSpan to miliseconds.
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public static int ToMilliSecondsInt(this TimeSpan time)
        {
            return (int) (time.Ticks)/TicksPerSecond;
        }

        /// <summary>
        ///     Gets the system uptime.
        /// </summary>
        /// <returns>the system uptime in milliseconds</returns>
        public static uint GetSystemTime()
        {
            return (uint) Environment.TickCount;
        }

        /// <summary>
        ///     Gets the date time from unix time.
        /// </summary>
        /// <param name="unixTime">The unix time.</param>
        /// <returns></returns>
        public static DateTime GetDateTimeFromUnixTime(uint unixTime)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(unixTime);
        }

        /// <summary>
        ///     Gets the UTC time from seconds.
        /// </summary>
        /// <param name="seconds">The seconds.</param>
        /// <returns></returns>
        public static DateTime GetUTCTimeSeconds(long seconds)
        {
            return UnixTimeStart.AddSeconds(seconds);
        }

        #endregion

        #region Objects
        /// <summary>
        ///     Swaps one reference with another atomically.
        /// </summary>
        /// <typeparam name="T">the type of the reference</typeparam>
        /// <param name="originalRef">the original reference</param>
        /// <param name="newRef">the new reference</param>
        public static void SwapReference<T>(ref T originalRef, ref T newRef) where T : class
        {
            T orig;

            do
            {
                orig = originalRef;
            } while (Interlocked.CompareExchange(ref originalRef, newRef, orig) != orig);
        }

        /// <summary>
        ///     Swaps one reference with another atomically, and replaces the original with the given value
        /// </summary>
        /// <typeparam name="T">the type of the reference</typeparam>
        /// <param name="originalRef">the original reference</param>
        /// <param name="newRef">the new reference</param>
        /// <param name="replacement">the value to replace the original with</param>
        public static void SwapReference<T>(ref T originalRef, ref T newRef, T replacement) where T : class
        {
            do
            {
                newRef = originalRef;
            } while (Interlocked.CompareExchange(ref originalRef, replacement, newRef) != newRef);
        }

        /// <summary>
        ///     Cast one thing into another
        /// </summary>
        public static T Cast<T>(object obj)
        {
            return (T) Convert.ChangeType(obj, typeof (T));
        }

        #endregion

        #region String Building / Verbosity

        /// <summary>
        ///     Returns the string representation of an IEnumerable (all elements, joined by comma)
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="conj">The conjunction to be used between each elements of the collection</param>
        public static string ToString<T>(this IEnumerable<T> collection, string conj)
        {
            string vals;
            if (collection != null)
            {
                vals = String.Join(conj, ToStringArrT(collection));
            }
            else
                vals = "(null)";

            return vals;
        }

        /// <summary>
        ///     Returns the string representation of an IEnumerable (all elements, joined by comma)
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="conj">The conjunction to be used between each elements of the collection</param>
        /// <param name="converter"></param>
        public static string ToString<T>(this IEnumerable<T> collection, string conj, Func<T, object> converter)
        {
            string vals;
            if (collection != null)
            {
                vals = String.Join(conj, ToStringArrT(collection, converter));
            }
            else
                vals = "(null)";

            return vals;
        }

        /// <summary>
        ///     Returns the string representation of an IEnumerable (all elements, joined by comma)
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="conj">The conjunction to be used between each elements of the collection</param>
        public static string ToStringCol(this ICollection collection, string conj)
        {
            string vals;
            if (collection != null)
            {
                vals = String.Join(conj, ToStringArr(collection));
            }
            else
                vals = "(null)";

            return vals;
        }

        //public static string[] ToStringArr(ICollection collection)
        //{
        //    var strArr = new string[collection.Count];
        //    var colEnum = collection.GetEnumerator();
        //    for (var i = 0; i < strArr.Length; i++)
        //    {
        //        colEnum.MoveNext();
        //        var cur = colEnum.Current;
        //        if (cur != null)
        //        {
        //            strArr[i] = cur.ToString();
        //        }
        //    }
        //    return strArr;
        //}

        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="conj">The conj.</param>
        /// <returns>
        ///     A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public static string ToString(this IEnumerable collection, string conj)
        {
            string vals;
            if (collection != null)
            {
                vals = String.Join(conj, ToStringArr(collection));
            }
            else
                vals = "(null)";

            return vals;
        }

        /// <summary>
        ///     Converts the collection to a string array.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <returns></returns>
        public static string[] ToStringArrT<T>(IEnumerable<T> collection)
        {
            return ToStringArrT(collection, null);
        }

        /// <summary>
        ///     Converts the collection to a string array.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <returns></returns>
        public static string[] ToStringArr(IEnumerable collection)
        {
            var strs = new List<string>();
            var colEnum = collection.GetEnumerator();
            while (colEnum.MoveNext())
            {
                var cur = colEnum.Current;
                if (cur != null)
                {
                    strs.Add(cur.ToString());
                }
            }
            return strs.ToArray();
        }

        /// <summary>
        ///     Converts the collection to a string array.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="converter"></param>
        /// <returns></returns>
        public static string[] ToStringArrT<T>(IEnumerable<T> collection, Func<T, object> converter)
        {
            var strArr = new string[collection.Count()];
            var colEnum = collection.GetEnumerator();
            var i = 0;
            while (colEnum.MoveNext())
            {
                var cur = colEnum.Current;
                if (!Equals(cur, default(T)))
                {
                    strArr[i++] = (converter != null ? converter(cur) : cur).ToString();
                }
            }
            return strArr;
        }

        /// <summary>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="col">The collection.</param>
        /// <param name="partCount">The part count.</param>
        /// <param name="conj">The conj.</param>
        /// <returns></returns>
        public static string[] ToJoinedStringArr<T>(IEnumerable<T> col, int partCount, string conj)
        {
            var strs = ToStringArrT(col);

            var list = new List<string>();
            var current = new List<string>(partCount);
            for (int index = 0, i = 0; index < strs.Length; i++, index++)
            {
                current.Add(strs[index]);
                if (i == partCount)
                {
                    i = 0;
                    list.Add(String.Join(conj, current.ToArray()));
                    current.Clear();
                }
            }
            if (current.Count > 0)
                list.Add(String.Join(conj, current.ToArray()));

            return list.ToArray();
        }

        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <typeparam name="K"></typeparam>
        /// <typeparam name="V"></typeparam>
        /// <param name="args">The args.</param>
        /// <param name="indent">The indent.</param>
        /// <param name="seperator">The seperator.</param>
        /// <returns>
        ///     A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public static string ToString<K, V>(this IEnumerable<KeyValuePair<K, V>> args, string indent, string seperator)
        {
            string s = "";
            var i = 0;
            foreach (var arg in args)
            {
                i++;
                s += indent + arg.Key + " = " + arg.Value;

                if (i < args.Count())
                {
                    s += seperator;
                }
            }
            return s;
        }

        #endregion

        #region Random

        private static long _holdrand = DateTime.Now.Ticks;

        /// <summary>
        ///     Returns a pseudo-random integer.
        /// </summary>
        /// <returns></returns>
        public static int Random()
        {
            return (int) (((_holdrand = _holdrand*214013L + 2531011L) >> 16) & 0x7fff);
        }

        /// <summary>
        ///     Returns a pseudo-random unsigned integer.
        /// </summary>
        /// <returns></returns>
        public static uint RandomUInt()
        {
            return (uint) (((_holdrand = _holdrand*214013L + 2531011L) >> 16) & 0x7fff);
        }

        /// <summary>
        ///     Returns a random chance value
        /// </summary>
        /// <returns>True or false</returns>
        public static bool Chance()
        {
            return Chance(RandomFloat());
        }

        /// <summary>
        ///     Returns a chance value using a seed
        /// </summary>
        /// <param name="chance">Chance seed</param>
        /// <returns>True or false</returns>
        public static bool Chance(double chance)
        {
            return chance > 1 ? true : chance < 0 ? false : Random()/(double) 0x7fff <= chance;
        }

        /// <summary>
        ///     Returns a chance value using a seed
        /// </summary>
        /// <param name="chance">Chance seed</param>
        /// <returns>True or false</returns>
        public static bool Chance(float chance)
        {
            return chance > 1 ? true : chance < 0 ? false : RandomFloat() <= chance;
        }

        /*public static bool Chance(int chance)
        {
            return chance >= 10000 ? true : random.Next(0, 10000) <= chance;
        }*/

        /// <summary>
        ///     Returns a random single-precision floating point number.
        /// </summary>
        /// <returns>Random number</returns>
        public static float RandomFloat()
        {
            return (((_holdrand = _holdrand*214013L + 2531011L) >> 16) & 0x7fff)/(float) 0x7fff;
        }

        /// <summary>
        ///     Generates a pseudo-random number in range [from, to)
        /// </summary>
        public static int Random(int from, int to)
        {
            //return from > to
            //        ? (int)Math.Round(RandomFloat() * (from - to) + to)
            //        : (int)Math.Round((RandomFloat() * (to - from) + from));
            return @from == to
                       ? @from
                       : (@from > to
                              ? ((Random()%(@from - to)) + to)
                              : ((Random()%(to - @from)) + @from));
        }


        /// <summary>
        ///     Returns a pseudo-random integer between the specified numbers..
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <returns></returns>
        public static uint Random(uint from, uint to)
        {
            //return from > to
            //        ? (uint)Math.Round((RandomFloat() * (from - to) + to))
            //        : (uint)Math.Round((RandomFloat() * (to - from) + from));return
            return @from == to
                       ? @from
                       : (@from > to
                              ? ((RandomUInt()%(@from - to)) + to)
                              : ((RandomUInt()%(to - @from)) + @from));
        }

        /// <summary>
        ///     Returns a pseudo-random integer.
        /// </summary>
        /// <param name="max">The maximum random value.</param>
        /// <returns></returns>
        public static int Random(int max)
        {
            //return from > to
            //        ? (uint)Math.Round((RandomFloat() * (from - to) + to))
            //        : (uint)Math.Round((RandomFloat() * (to - from) + from));return
            return Random()%max;
        }

        /// <summary>
        ///     Returns a pseudo-random unsigned integer.
        /// </summary>
        /// <param name="max">The maximum random value.</param>
        /// <returns></returns>
        public static uint RandomUInt(uint max)
        {
            //return from > to
            //        ? (uint)Math.Round((RandomFloat() * (from - to) + to))
            //        : (uint)Math.Round((RandomFloat() * (to - from) + from));return
            return RandomUInt()%max;
        }

        /// <summary>
        ///     Returns a random single-precision floating point number.
        /// </summary>
        /// <returns>Random number</returns>
        public static float Random(float from, float to)
        {
            return @from > to ? RandomFloat()*(@from - to) + to : (RandomFloat()*(to - @from) + @from);
        }

        /// <summary>
        ///     Returns a random double-precision floating point number.
        /// </summary>
        /// <returns>Random number</returns>
        public static double Random(double from, double to)
        {
            return @from > to ? RandomFloat()*(@from - to) + to : RandomFloat()*(to - @from) + @from;
        }

        #endregion

        #region Simple Arbitrary String Parsing

        private static readonly Dictionary<Type, Func<string, object>> TypeParsers =
            new Func<Dictionary<Type, Func<string, object>>>(() =>
                {
                    var parsers =
                        new Dictionary<Type, Func<string, object>>
                            {
                                {
                                    typeof (int),
                                    strVal => Int32.Parse(strVal)
                                },
                                {
                                    typeof (float),
                                    strVal => Single.Parse(strVal)
                                },
                                {
                                    typeof (long),
                                    strVal => Int64.Parse(strVal)
                                },
                                {
                                    typeof (ulong),
                                    strVal => UInt64.Parse(strVal)
                                },
                                {
                                    typeof (bool), strVal =>
                                                   strVal.Equals(
                                                       "true",
                                                       StringComparison.InvariantCultureIgnoreCase) ||
                                                   strVal.Equals("1",
                                                                 StringComparison.InvariantCultureIgnoreCase) ||
                                                   strVal.Equals("yes",
                                                                 StringComparison.InvariantCultureIgnoreCase)
                                },
                                {
                                    typeof (double),
                                    strVal => Double.Parse(strVal)
                                },
                                {
                                    typeof (uint),
                                    strVal => UInt32.Parse(strVal)
                                },
                                {
                                    typeof (short),
                                    strVal => Int16.Parse(strVal)
                                },
                                {
                                    typeof (ushort),
                                    strVal => Int16.Parse(strVal)
                                },
                                {
                                    typeof (byte),
                                    strVal => Byte.Parse(strVal)
                                },
                                {typeof (char), strVal => strVal[0]}
                            };

                    return parsers;
                })();

        /// <summary>
        ///     Parses the specified string as the type.
        /// </summary>
        /// <param name="stringVal">The string val.</param>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public static object Parse(string stringVal, Type type)
        {
            object obj = null;
            if (!Parse(stringVal, type, ref obj))
            {
                throw new Exception(String.Format("Unable to parse string-Value \"{0}\" as Type \"{1}\"", stringVal,
                                                  type));
            }
            return obj;
        }

        /// <summary>
        ///     Parses the specified STR.
        /// </summary>
        /// <param name="str">The STR.</param>
        /// <param name="type">The type.</param>
        /// <param name="obj">The obj.</param>
        /// <returns></returns>
        public static bool Parse(string str, Type type, ref object obj)
        {
            Contract.Requires(str != null);

            if (type == typeof (string))
            {
                obj = str;
            }
            else if (type.IsEnum)
            {
                try
                {
                    obj = Enum.Parse(type, str, true);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                Func<string, object> parser;
                if (TypeParsers.TryGetValue(type, out parser))
                {
                    try
                    {
                        obj = parser(str);
                        return obj != null;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            }
            return true;
        }

        #endregion

        /// <summary>
        ///     Gets the biggest value of a numeric enum
        /// </summary>
        public static T GetMaxEnum<T>()
        {
            var values = (T[]) Enum.GetValues(typeof (T));
            return values.Max();
        }


        /// <summary>
        ///     Creates the enum array.
        /// </summary>
        /// <typeparam name="E"></typeparam>
        /// <typeparam name="A"></typeparam>
        /// <returns></returns>
        public static A[] CreateEnumArray<E, A>()
        {
            var arr = new A[(int) Convert.ChangeType(GetMaxEnum<E>(), typeof (int))];
            return arr;
        }

        /// <summary>
        ///     Delays the given action by the given amount of milliseconds
        /// </summary>
        /// <returns>The timer that performs the delayed call (in case that you might want to cancel earlier)</returns>
        public static Timer Delay(uint millis, Action action)
        {
            Timer timer = null;
            timer = new Timer(sender =>
                {
                    action();
                    timer.Dispose();
                });
            timer.Change(millis, Timeout.Infinite);
            return timer;
        }

        #region Evaluate etc

#pragma warning disable 1591

        public delegate T OperatorHandler<T>(T x, T y);

        public static readonly OperatorHandler<long> BinaryOrHandler =
            (x, y) => x | y;

        public static readonly OperatorHandler<long> BinaryXOrHandler =
            (x, y) => x & ~y;

        public static readonly OperatorHandler<long> BinaryAndHandler =
            (x, y) => x & y;

        public static readonly OperatorHandler<long> PlusHandler =
            (x, y) => x + y;

        public static readonly OperatorHandler<long> MinusHandler =
            (x, y) => x - y;

        public static readonly OperatorHandler<long> DivideHandler =
            (x, y) => x/y;

        public static readonly OperatorHandler<long> MultiHandler =
            (x, y) => x*y;

        public static readonly Dictionary<string, OperatorHandler<long>> IntOperators =
            new Dictionary<string, OperatorHandler<long>>();

#pragma warning restore 1591

        /// <summary>
        ///     Evaluates the given (simple) expression
        ///     TODO: Use Polish Notation to allow more efficiency and complexity
        ///     TODO: Add operator priority
        /// </summary>
        public static bool Eval(Type valType, ref long val, string expr, ref object error, bool startsWithOperator)
        {
            // syntax: <val> <op> <value> [<op> <value> [<op> <value>...]]
            var args = expr.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            var isOp = startsWithOperator;
            OperatorHandler<long> op = null;
            foreach (var argument in args)
            {
                var arg = argument.Trim();
                if (isOp)
                {
                    if (!IntOperators.TryGetValue(arg, out op))
                    {
                        error = "Invalid operator: " + arg;
                        return false;
                    }
                }
                else
                {
                    object argVal = null;
                    if (!Parse(arg, valType, ref argVal))
                    {
                        error = "Could not convert value \"" + arg + "\" to Type \"" + valType + "\"";
                        return false;
                    }

                    var longVal = (long) Convert.ChangeType(argVal, typeof (long));
                    if (op != null)
                    {
                        val = op(val, longVal);
                    }
                    else
                    {
                        val = longVal;
                    }
                }
                isOp = !isOp;
            }
            return true;
        }

        #endregion

        /// <summary>
        ///     Gets the absolute path.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <returns></returns>
        public static string GetAbsolutePath(string file)
        {
            return new DirectoryInfo(file).FullName;
        }

        /// <summary>
        ///     Parses or resolves the specified IP address.
        /// </summary>
        /// <param name="input">The input IP.</param>
        /// <returns></returns>
        public static IPAddress ParseOrResolve(string input)
        {
            IPAddress addr;
            if (IPAddress.TryParse(input, out addr))
            {
                return addr;
            }

            // try resolve synchronously
            var addresses = Dns.GetHostAddresses(input);

            // for now only do Ipv4 address (apparently the wow client doesnt support Ipv6 yet)
            addr = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);

            return addr ?? IPAddress.Loopback;
        }

        #region Format

        /// <summary>
        ///     Formats the specified time.
        /// </summary>
        /// <param name="time">The time.</param>
        /// <returns></returns>
        public static string Format(this TimeSpan time)
        {
            return String.Format("{0}{1:00}h {2:00}m {3:00}s", time.TotalDays > 0 ? (int) time.TotalDays + "d " : "",
                                 time.Hours, time.Minutes, time.Seconds);
        }

        #endregion

        /// <summary>
        ///     Gets a random element from the list.
        /// </summary>
        /// <typeparam name="TO"></typeparam>
        /// <param name="os">The os.</param>
        /// <returns></returns>
        public static TO GetRandom<TO>(this IList<TO> os)
        {
            return os.Count == 0 ? default(TO) : os[Random(0, os.Count)];
        }

        /// <summary>
        ///     Checks whether the given mail-address is valid.
        /// </summary>
        public static bool IsValidEMailAddress(string mail)
        {
            return EmailAddressParser.Valid(mail, false);
        }

        #region Types

        /// <summary>
        ///     Determines whether the specified type is static.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>
        ///     <c>true</c> if the specified type is static; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsStatic(this Type type)
        {
            return (type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) ==
                   (TypeAttributes.Abstract | TypeAttributes.Sealed);
        }

        /// <summary>
        ///     When overridden in a derived class, returns an array of custom attributes identified by System.Type.
        /// </summary>
        /// <typeparam name="T">The type of attribute to search for. Only attributes that are assignable to this type are returned.</typeparam>
        /// <param name="methodInfo"></param>
        /// <returns>An array of custom attributes applied to this member, or an array with zero (0) elements if no attributes have been applied.</returns>
        public static T[] GetCustomAttributes<T>(this MemberInfo methodInfo) where T : Attribute
        {
            return methodInfo.GetCustomAttributes(typeof (T), false) as T[];
        }

        /// <summary>
        /// </summary>
        /// <param name="arrType"></param>
        /// <returns></returns>
        public static Type GetArrUnderlyingType(Type arrType)
        {
            var name = arrType.FullName;
            var index = name.IndexOf('[');
            if (index > -1)
            {
                name = name.Substring(0, index);
                var nonArrType = arrType.Assembly.GetType(name);
                return nonArrType;
            }
            return null;
        }

        /// <summary>
        ///     One second has 10 million system ticks (DateTime.Ticks etc)
        /// </summary>
        private const string DefaultNameSpace = "WCell.Constants.";

        /// <summary>
        ///     Gets the type.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <returns></returns>
        public static Type GetType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type == null)
            {
                if (!TypeMap.TryGetValue(typeName, out type) &&
                    !TypeMap.TryGetValue(DefaultNameSpace + typeName, out type))
                {
                    throw new Exception("Invalid Type specified: " + typeName);
                }
            }
            return type;
        }

        /// <summary>
        ///     Gets all assemblies that match the given fully qualified name without version checks etc.
        /// </summary>
        /// <param name="asmName"></param>
        /// <returns></returns>
        public static IEnumerable<Assembly> GetMatchingAssemblies(string asmName)
        {
            var parts = asmName.Split(',');
            if (parts.Length > 0)
            {
                asmName = parts[0];
            }
            return AppDomain.CurrentDomain.GetAssemblies().Where(asm =>
                {
                    var matchName = asm.GetName();
                    return matchName.Name == asmName;
                });
        }

        /// <summary>
        ///     Changes the type.
        /// </summary>
        /// <param name="obj">The obj.</param>
        /// <param name="type">The type.</param>
        /// <param name="underlyingType">
        ///     if set to <c>true</c> [underlying type].
        /// </param>
        /// <returns></returns>
        public static object ChangeType(object obj, Type type, bool underlyingType = false)
        {
            if (type.IsEnum)
            {
                //obj = Enum.Parse(type, obj.ToString());
                var uType = Enum.GetUnderlyingType(type);
                if (!underlyingType)
                {
                    obj = Enum.ToObject(type, obj);
                }
                else if (uType != obj.GetType())
                {
                    obj = Convert.ChangeType(obj, uType);
                }
                return obj;
            }
            // try to find a ctor
            var ctor = type.GetConstructor(new[] {obj.GetType()});

            if (ctor == null)
            {
                try
                {
                    return Convert.ChangeType(obj, type);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(String.Format(
                        "Could not convert \"{0}\" from {1} to {2} - {2} has no public ctor with one argument of type \"{1}\".",
                        obj, obj.GetType(), type), e);
                }
            }

            return ctor.Invoke(new[] {obj});
        }

        #endregion

        #region Files & Directories

        /// <summary>
        ///     Writes the content of all files in the given directory to the given output file
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="outputFile"></param>
        public static void MergeFiles(string directory, string outputFile)
        {
            MergeFiles(Directory.GetFiles(directory), outputFile);
        }

        /// <summary>
        ///     Writes the content of all files in the given list to the given output file
        /// </summary>
        public static void MergeFiles(IEnumerable<string> inputFiles, string outputFile)
        {
            using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
            {
                using (var strOutput = new StreamWriter(output))
                {
                    foreach (var file in inputFiles)
                    {
                        var content = File.ReadAllBytes(file);

                        strOutput.WriteLine();
                        strOutput.WriteLine("# " + file);
                        strOutput.WriteLine();
                        strOutput.Flush();
                        output.Write(content, 0, content.Length);
                        output.Flush();
                    }
                }
            }
        }

        /// <summary>
        ///     Gets the directory.
        /// </summary>
        /// <param name="file">The fileinfo.</param>
        /// <returns></returns>
        public static DirectoryInfo GetDirectory(this FileSystemInfo file)
        {
            if (file is DirectoryInfo)
            {
                return ((DirectoryInfo) file).Parent;
            }
            if (file is FileInfo)
            {
                return ((FileInfo) file).Directory;
            }
            return null;
        }

        /// <summary>
        ///     Creates directories.
        /// </summary>
        /// <param name="file"></param>
        public static void MKDirs(this FileInfo file)
        {
            MKDirs(file.GetDirectory());
        }

        /// <summary>
        ///     Creates directories.
        /// </summary>
        /// <param name="dir"></param>
        public static void MKDirs(this DirectoryInfo dir)
        {
            if (!dir.Exists)
            {
                var parent = dir.Parent;
                if (parent != null && !parent.Exists)
                {
                    MKDirs(dir.Parent);
                }
                dir.Create();
            }
        }

        /// <summary>
        ///     Returns up to the n first lines from the given file.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="n"></param>
        /// <param name="ignoreEmpty"></param>
        /// <returns></returns>
        public static string[] ReadLines(string fileName, int n, bool ignoreEmpty)
        {
            var lines = new string[n];
            using (var reader = new StreamReader(fileName))
            {
                for (var i = 0; i < n; i++)
                {
                    if (reader.EndOfStream)
                    {
                        break;
                    }
                    lines[i] = reader.ReadLine();
                    if (ignoreEmpty && lines[i].Length == 0)
                    {
                        i--;
                    }
                }
            }
            return lines;
        }

        #endregion

        #region Strings

        /// <summary>
        ///     Gets the string representation of the specified object.
        /// </summary>
        /// <param name="val">The object.</param>
        /// <returns></returns>
        public static string GetStringRepresentation(object val)
        {
            var s = val as string;
            if (s != null)
            {
                return s;
            }
            var collection = val as ICollection;
            if (collection != null)
            {
                return collection.ToStringCol(", ");
            }
            var enumerable = val as IEnumerable;
            if (enumerable != null)
            {
                return enumerable.ToString(", ");
            }
            if (val is TimeSpan)
            {
                return ((TimeSpan) val).Format();
            }
            return val.ToString();
        }


        /// <summary>
        ///     Determines whether the string contains the specified string, ignroing culture and case.
        /// </summary>
        /// <param name="str">The STR.</param>
        /// <param name="part">The part.</param>
        /// <returns>
        ///     <c>true</c> if contains; otherwise, <c>false</c>.
        /// </returns>
        public static bool ContainsIgnoreCase(this string str, string part)
        {
            return str.IndexOf(part, StringComparison.InvariantCultureIgnoreCase) > -1;
        }

        #endregion

        private static readonly Random Rnd = new Random();
        private static readonly List<Admin> Admins = new List<Admin>();

        /// <summary>
        ///     Shuffles the specified collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="col">The collection.</param>
        public static void Shuffle<T>(ICollection<T> col)
        {
            var arr = col.ToArray();
            var b = new byte[arr.Length];
            Rnd.NextBytes(b);
            Array.Sort(b, arr);
            col.Clear();
            foreach (var item in arr)
            {
                col.Add(item);
            }
        }

        /// <summary>
        ///     Calculates the MD5 sum of a file.
        /// </summary>
        /// <param name="fileName">
        ///     The file to check.
        /// </param>
        /// <returns>
        ///     The MD5 hash.
        /// </returns>
        public static string MD5File(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException("fileName");
            byte[] retVal;

            using (var file = new FileStream(fileName, FileMode.Open))
            {
                MD5 md5 = new MD5CryptoServiceProvider();
                retVal = md5.ComputeHash(file);
                md5.Dispose();
            }

            var sb = new StringBuilder();

            if (retVal != null)
                for (var i = 0; i < retVal.Length; i++)
                    sb.Append(retVal[i].ToString("x2"));

            return sb.ToString();
        }

        /// <summary>
        ///     Calculates the MD5 hash of a string.
        /// </summary>
        /// <param name="value">
        ///     The string to calculate MD5 hash of.
        /// </param>
        /// <returns>
        ///     The MD5 hash.
        /// </returns>
        public static string MD5String(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            var x = new MD5CryptoServiceProvider();

            var data = Encoding.ASCII.GetBytes(value);
            data = x.ComputeHash(data);
            x.Dispose();
            var ret = "";

            for (var i = 0; i < data.Length; i++)
                ret += data[i].ToString("x2").ToLower();

            return ret;
        }

        /// <summary>
        /// The Bot operator.
        /// </summary>
        public static Config.Operator Operator { get; set; }

        /// <summary>
        ///     Sends system stats using the specified connection.
        /// </summary>
        /// <param name="chan">
        ///     The channel to send to.
        /// </param>
        public static void SendSysStats(string chan)
        {
            //Log.Info("System info request.");
            var connection = AlarisBase.Instance.Connection;

            var hostname = Environment.MachineName;
            var username = Environment.UserName;

            var os = Environment.OSVersion.ToString();
            var mem = Process.GetCurrentProcess().PrivateMemorySize64/1024/1024;
            


            connection.Sender.PublicMessage(chan,
                                            IrcConstants.Bold + "Bot version: " + IrcConstants.Normal +
                                            BotVersion + " (running on: " + os + ")");
            connection.Sender.PublicMessage(chan,
                                            IrcConstants.Bold + "Thread count: " + IrcConstants.Normal +
                                            Process.GetCurrentProcess().Threads.Count);
            connection.Sender.PublicMessage(chan,
                                            IrcConstants.Bold + "CPU: " + IrcConstants.Normal + GetCpuId() + " | " +
                                            Environment.ProcessorCount + " cores.");

            if (mem < 60)
                connection.Sender.PublicMessage(chan,
                                                String.Format("{0}Memory used: {1}{2}{3} MB",
                                                              IrcConstants.Bold, IrcConstants.Normal, IrcConstants.Green,
                                                              mem));
            else if (mem > 60 && mem < 80)
                connection.Sender.PublicMessage(chan,
                                                String.Format("{0}Memory used: {1}{2}{3} MB",
                                                              IrcConstants.Bold, IrcConstants.Normal, IrcConstants.Olive,
                                                              mem));
            else
                connection.Sender.PublicMessage(chan,
                                                String.Format("{0}Memory used: {1}{2}{3} MB",
                                                              IrcConstants.Bold, IrcConstants.Normal, IrcConstants.Red,
                                                              mem));
        }

        /// <summary>
        ///     Sends info using the specified connection.
        /// </summary>
        /// <param name="chan">
        ///     The channel to send to.
        /// </param>
        public static void SendInfo(string chan)
        {
            var connection = AlarisBase.Instance.Connection;
            connection.Sender.PublicMessage(chan, IrcConstants.Cyan + "Alaris " + BotVersion);
            connection.Sender.PublicMessage(chan, IrcConstants.DarkGreen + "Developer: Twl");
        }

        /// <summary>
        ///     Determines whether the specified user is admin or not.
        /// </summary>
        /// <param name="user">
        ///     The user to check.
        /// </param>
        /// <returns>
        ///     True if admin, otherwise false.
        /// </returns>
        public static bool IsAdmin(UserInfo user)
        {
            if (user == null) throw new ArgumentNullException("user");
            return ((IsMainAdmin(user)) || user == CLI.ConsoleUser);
        }

        private static bool IsMainAdmin(UserInfo user)
        {
            return user.Hostname.Equals(Operator.Host, StringComparison.InvariantCultureIgnoreCase) && user.Nick.Equals(Operator.Nick, StringComparison.InvariantCultureIgnoreCase) 
                && user.User.Equals(Operator.User, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        ///     Gets the cpu brand string.
        /// </summary>
        /// <returns>
        ///     The CPU brand string.
        /// </returns>
        private static string GetCpuId()
        {
#if !__MonoCS__
            var mos = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Processor");

            return
                (from ManagementObject mo in mos.Get() select (Regex.Replace(Convert.ToString(mo["Name"]), @"\s+", " ")))
                    .FirstOrDefault();
#else
            return "Not available.";
#endif
        }

        /// <summary>
        ///     Gets the URLs in the specified text.
        /// </summary>
        /// <param name="text">
        ///     The text to search in.
        /// </param>
        /// <returns>
        ///     The list of urls.
        /// </returns>
        public static List<string> GetUrls(string text)
        {
            Contract.Requires(text != null);
            Contract.Ensures(Contract.Result<List<string>>() != null);

            var urls = new List<string>();

            try
            {
                //var urlFind = new Regex(@"(?<page>http://(www\.)?\S+\.\S{2,6}(/\S*\s*)?)", RegexOptions.IgnoreCase);
                //var urlFind = new Regex(@"(?<page>http://(www\.)?\S+\.\S{2,6}(/*\S*))", RegexOptions.IgnoreCase);

                var urlFind = new Regex(@"(?<url>(http://)?(www\.)?\S+\.\S{2,6}([/]*\S+))",
                                        RegexOptions.Compiled | RegexOptions.IgnoreCase);

                if (urlFind.IsMatch(text))
                {
                    var matches = urlFind.Matches(text);

                    //urls.AddRange(from Match match in matches select match.Groups["page"].ToString());

                    foreach (var url in from Match match in matches select match.Groups["url"].ToString())
                    {
                        var lurl = url;
                        if (!lurl.StartsWith("http://") && !url.StartsWith("https://"))
                            lurl = string.Format("http://{0}", url);

                        Log.Debug("Utilities", string.Format("Checking: {0}", url));

                        urls.Add(lurl);
                    }
                }
            }
            catch (Exception x)
            {
                Log.Error(x.Message);
            }

            return urls;
        }

        /// <summary>
        ///     Handles the web title command.
        /// </summary>
        /// <param name="chan">
        ///     The channel to send title to.
        /// </param>
        /// <param name="msg">
        ///     The message containing the url.
        /// </param>
        public static void HandleWebTitle(string chan, string msg)
        {
            if (chan == null) throw new ArgumentNullException("chan");
            if (msg == null) throw new ArgumentNullException("msg");

            var connection = AlarisBase.Instance.Connection;

            try
            {
                var tt = msg.Replace("@title ", string.Empty);

                var url = new Uri(tt);
                var webTitle = WebHelper.GetWebTitle(url);

                if (string.IsNullOrEmpty(webTitle))
                    return;

                var title = Regex.Replace(webTitle, @"\s+", " ");


                // check if it's youtube.
                var youtubeRegex = new Regex(@"(?<song>.+)\-\sYouTube", RegexOptions.IgnoreCase);

                if (youtubeRegex.IsMatch(title))
                {
                    var match = youtubeRegex.Match(title);
                    var song = match.Groups["song"].ToString();

                    lock (SendLock)
                    {
                        connection.Sender.PublicMessage(chan,
                                                        IrcConstants.Purple + "[YouTube]: " + IrcConstants.DarkGreen +
                                                        song.Substring(1));
                        // about substr: remove the space before song name
                    }
                    return;
                }

                lock (SendLock)
                {
                    Log.Debug("WebHelper", string.Format("Title: {0}", title));
                    connection.Sender.PublicMessage(chan,
                                                    IrcConstants.Bold + "[Title]: " + IrcConstants.Normal +
                                                    IrcConstants.DarkGreen + title);
                }
            }
            catch (Exception x)
            {
                Log.Debug("Utilities", x.Message);
                return;
            }
        }

        /// <summary>
        ///     Executes the provided action safely.
        /// </summary>
        /// <param name="action">The action.</param>
        public static void ExecuteSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception x)
            {
                Log.Error("An exception has been thrown inside the safe execution context. ({0})", x.Message);
                return;
            }
        }

        /// <summary>
        ///     Downloads the specified web page's source as string.
        ///     <para>Shouldn't be used to download files (only text-based).</para>
        /// </summary>
        /// <param name="address">The URL to download.</param>
        /// <returns>The downloaded source</returns>
        public static string GetWebsiteString(string address)
        {
            try
            {
                var url = new Uri(address);

                return GetWebsiteString(url);
            }
            catch (UriFormatException x)
            {
                Log.Error("Invalid url received as argument ({0}). Exception: {1}", address, x);
                return string.Empty;
            }
        }

        /// <summary>
        ///     Downloads the specified web page's source as string.
        ///     <para>Shouldn't be used to download files (only text-based).</para>
        /// </summary>
        /// <param name="address">The URL to download.</param>
        /// <returns>The downloaded source</returns>
        public static string GetWebsiteString(Uri address)
        {
            try
            {
                using (var client = new WebClient())
                {
                    return client.DownloadString(address);
                }
            }
            catch (WebException x)
            {
                Log.Error("Exception thrown while downloading a website's source! Exception: {0}", x);
                return string.Empty;
            }
        }
    }

    #region SingleEnumerator

    /// <summary>
    ///     Returns a single element
    /// </summary>
    public class SingleEnumerator<T> : IEnumerator<T>
        where T : class
    {
        private T _current;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SingleEnumerator&lt;T&gt;" /> class.
        /// </summary>
        /// <param name="element">The element.</param>
        public SingleEnumerator(T element)
        {
            Current = element;
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Current = null;
        }

        /// <summary>
        ///     Advances the enumerator to the next element of the collection.
        /// </summary>
        /// <returns>
        ///     true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.
        /// </returns>
        /// <exception cref="T:System.InvalidOperationException">The collection was modified after the enumerator was created. </exception>
        public bool MoveNext()
        {
            return Current != null;
        }

        /// <summary>
        ///     Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        /// <exception cref="T:System.InvalidOperationException">The collection was modified after the enumerator was created. </exception>
        public void Reset()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     The current enumeration value
        /// </summary>
        public T Current
        {
            get
            {
                var current = _current;
                _current = null;
                return current;
            }
            private set { _current = value; }
        }

        /// <summary>
        ///     Gets the current.
        /// </summary>
        /// <value>The current.</value>
        object IEnumerator.Current
        {
            get { return Current; }
        }
    }

    #endregion

    // ReSharper restore MemberCanBePrivate.Global
}