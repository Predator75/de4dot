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

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	// Find the assembly resolver that's used in lib mode (3.8+)
	class LibAssemblyResolver {
		ModuleDefinition module;
		MethodDefinition initMethod;
		List<EmbeddedResource> resources = new List<EmbeddedResource>();

		public TypeDefinition Type {
			get { return initMethod == null ? null : initMethod.DeclaringType; }
		}

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public IEnumerable<EmbeddedResource> Resources {
			get { return resources; }
		}

		public LibAssemblyResolver(ModuleDefinition module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (checkInitMethod(DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor"), simpleDeobfuscator, deob))
				return;
			if (checkInitMethod(module.EntryPoint, simpleDeobfuscator, deob))
				return;
		}

		bool checkInitMethod(MethodDefinition checkMethod, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var requiredFields = new string[] {
				"System.Collections.Hashtable",
				"System.Boolean",
			};

			foreach (var tuple in DotNetUtils.getCalledMethods(module, checkMethod)) {
				var method = tuple.Item2;

				if (method.Body == null)
					continue;
				if (!method.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;

				var type = method.DeclaringType;
				if (!new FieldTypes(type).exactly(requiredFields))
					continue;
				var ctor = DotNetUtils.getMethod(type, ".ctor");
				if (ctor == null)
					continue;
				var handler = getHandler(ctor);
				if (handler == null)
					continue;
				simpleDeobfuscator.decryptStrings(handler, deob);
				var resourcePrefix = getResourcePrefix(handler);
				if (resourcePrefix == null)
					continue;

				for (int i = 0; ; i++) {
					var resource = DotNetUtils.getResource(module, resourcePrefix + i.ToString("D5")) as EmbeddedResource;
					if (resource == null)
						break;
					resources.Add(resource);
				}

				initMethod = method;
				return true;
			}

			return false;
		}

		MethodDefinition getHandler(MethodDefinition ctor) {
			if (ctor == null || ctor.Body == null)
				return null;
			foreach (var instr in ctor.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldftn)
					continue;
				var handler = instr.Operand as MethodReference;
				if (handler == null)
					continue;
				if (!DotNetUtils.isMethod(handler, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					continue;
				var handlerDef = DotNetUtils.getMethod(module, handler);
				if (handlerDef == null)
					continue;

				return handlerDef;
			}

			return null;
		}

		string getResourcePrefix(MethodDefinition handler) {
			foreach (var s in DotNetUtils.getCodeStrings(handler)) {
				var resource = DotNetUtils.getResource(module, s + "00000") as EmbeddedResource;
				if (resource != null)
					return s;
			}
			return null;
		}
	}
}
