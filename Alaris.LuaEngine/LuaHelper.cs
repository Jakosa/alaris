﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using LuaInterface;

namespace Alaris.LuaEngine
{
    /// <summary>
    /// Helper functions for the LuaEngine implementation.
    /// </summary>
    public static class LuaHelper
    {
        /// <summary>
        /// Registers Lua functions found in the specified target.
        /// </summary>
        /// <param name="luaFunctions">Global lua function table.</param>
        /// <param name="target">Object (class,struct) to search in.</param>
        /// <param name="vm">The Lua virtual machine.</param>
        public static void RegisterLuaFunctions(Lua vm, ref Dictionary<string, LuaFunctionDescriptor> luaFunctions, object target)
        {
            if (vm == null || luaFunctions == null)
                return;

            var type = target.GetType();

            foreach(var method in type.GetMethods())
            {
                foreach(var attribute in Attribute.GetCustomAttributes(method))
                {
                    if(attribute.GetType() == typeof(LuaFunctionAttribute))
                    {
                        var attr = (LuaFunctionAttribute) attribute;

                        var parameters = new List<string>();

                        var paramInfo = method.GetParameters();

                        if(attr.FunctionParameters != null && paramInfo.Length != attr.FunctionParameters.Length)
                        {
                            Console.Error.WriteLine("Function {0} (exported as {1}): argument number mismatch. Declared {2}, but requires {3}.", 
                                method.Name, 
                                attr.FunctionName, 
                                attr.FunctionParameters.Length,
                                paramInfo.Length);

                            break;
                        }

                        // build parameter doc hashtable.
                        if (attr.FunctionParameters != null)
                            parameters.AddRange(paramInfo.Select((t, i) => string.Format("{0} - {1}", t.Name, attr.FunctionParameters[i])));

                        var descriptor = new LuaFunctionDescriptor(attr.FunctionName, attr.FunctionDocumentation,
                                                                   parameters);

                        luaFunctions.Add(attr.FunctionName, descriptor);

                        vm.RegisterFunction(attr.FunctionName, target, method);
                    }
                }
            }
        }
    }
}