﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	abstract class ResolverInfoBase {
		protected ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IDeobfuscator deob;
		TypeDefinition resolverType;
		MethodDefinition callResolverMethod;

		public TypeDefinition Type {
			get { return resolverType; }
		}

		public TypeDefinition CallResolverType {
			get {
				if (callResolverMethod == null)
					return null;
				if (!hasOnlyThisMethod(callResolverMethod.DeclaringType, callResolverMethod))
					return null;
				return callResolverMethod.DeclaringType;
			}
		}

		public MethodDefinition CallResolverMethod {
			get { return callResolverMethod; }
		}

		public ResolverInfoBase(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public bool findTypes() {
			if (resolverType != null)
				return true;

			if (findTypes(DotNetUtils.getModuleTypeCctor(module)))
				return true;
			if (findTypes(module.EntryPoint))
				return true;

			return false;
		}

		bool findTypes(MethodDefinition initMethod) {
			if (initMethod == null)
				return false;
			foreach (var tuple in DotNetUtils.getCalledMethods(module, initMethod)) {
				var method = tuple.Item2;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (checkAttachAppMethod(method))
					return true;
			}

			return false;
		}

		bool checkAttachAppMethod(MethodDefinition attachAppMethod) {
			callResolverMethod = null;
			if (!attachAppMethod.HasBody)
				return false;

			foreach (var tuple in DotNetUtils.getCalledMethods(module, attachAppMethod)) {
				var method = tuple.Item2;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (!checkResolverInitMethod(method))
					continue;

				callResolverMethod = attachAppMethod;
				return true;
			}

			if (hasLdftn(attachAppMethod)) {
				simpleDeobfuscator.deobfuscate(attachAppMethod);
				foreach (var resolverHandler in getResolverHandlers(attachAppMethod)) {
					if (!resolverHandler.HasBody)
						continue;
					var resolverTypeTmp = getResolverType(resolverHandler);
					if (resolverTypeTmp == null)
						continue;
					deobfuscate(resolverHandler);
					if (checkHandlerMethod(resolverHandler)) {
						callResolverMethod = attachAppMethod;
						resolverType = resolverTypeTmp;
						return true;
					}
				}
			}

			return false;
		}

		static bool hasLdftn(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldftn)
					return true;
			}
			return false;
		}

		bool checkResolverInitMethod(MethodDefinition initMethod) {
			resolverType = null;
			if (!initMethod.HasBody)
				return false;

			deobfuscate(initMethod);
			foreach (var handlerDef in getResolverHandlers(initMethod)) {
				deobfuscate(handlerDef);

				var resolverTypeTmp = getResolverType(handlerDef);
				if (resolverTypeTmp == null)
					continue;

				if (checkHandlerMethod(handlerDef)) {
					resolverType = resolverTypeTmp;
					return true;
				}
			}

			return false;
		}

		void deobfuscate(MethodDefinition method) {
			simpleDeobfuscator.deobfuscate(method);
			simpleDeobfuscator.decryptStrings(method, deob);
		}

		TypeDefinition getResolverType(MethodDefinition resolveHandler) {
			if (resolveHandler.Body == null)
				return null;
			foreach (var instr in resolveHandler.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldsfld && instr.OpCode.Code != Code.Stsfld)
					continue;
				var field = DotNetUtils.getField(module, instr.Operand as FieldReference);
				if (field == null)
					continue;
				if (!checkResolverType(field.DeclaringType))
					continue;

				return field.DeclaringType;
			}

			if (checkResolverType(resolveHandler.DeclaringType))
				return resolveHandler.DeclaringType;

			return null;
		}

		protected abstract bool checkResolverType(TypeDefinition type);
		protected abstract bool checkHandlerMethod(MethodDefinition handler);

		IEnumerable<MethodDefinition> getResolverHandlers(MethodDefinition method) {
			int numHandlers = 0;
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Call, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj, OpCodes.Callvirt);
				if (instrs == null)
					continue;

				var call = instrs[0];
				if (!DotNetUtils.isMethod(call.Operand as MethodReference, "System.AppDomain", "()"))
					continue;

				var ldftn = instrs[2];
				var handlerDef = DotNetUtils.getMethod(module, ldftn.Operand as MethodReference);
				if (handlerDef == null)
					continue;

				var newobj = instrs[3];
				if (!DotNetUtils.isMethod(newobj.Operand as MethodReference, "System.Void", "(System.Object,System.IntPtr)"))
					continue;

				var callvirt = instrs[4];
				if (!DotNetUtils.isMethod(callvirt.Operand as MethodReference, "System.Void", "(System.ResolveEventHandler)"))
					continue;

				numHandlers++;
				yield return handlerDef;
			}

			// If no handlers found, it's possible that the method itself is the handler.
			if (numHandlers == 0)
				yield return method;
		}

		static bool hasOnlyThisMethod(TypeDefinition type, MethodDefinition method) {
			if (type == null || method == null)
				return false;
			foreach (var m in type.Methods) {
				if (m.Name == ".cctor" || m.Name == ".ctor")
					continue;
				if (m == method)
					continue;
				return false;
			}
			return true;
		}
	}
}
