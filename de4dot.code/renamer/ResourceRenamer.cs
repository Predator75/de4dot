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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.code.renamer.asmmodules;

namespace de4dot.code.renamer {
	class ResourceRenamer {
		Module module;
		Dictionary<string, Resource> nameToResource;

		public ResourceRenamer(Module module) {
			this.module = module;
		}

		public void rename(List<TypeInfo> renamedTypes) {
			// Rename the longest names first. Otherwise eg. b.g.resources could be renamed
			// Class0.g.resources instead of Class1.resources when b.g was renamed Class1.
			renamedTypes.Sort((a, b) => Utils.compareInt32(b.oldFullName.Length, a.oldFullName.Length));

			nameToResource = new Dictionary<string, Resource>(module.ModuleDefinition.Resources.Count * 3, StringComparer.Ordinal);
			foreach (var resource in module.ModuleDefinition.Resources) {
				var name = resource.Name;
				nameToResource[name] = resource;
				if (name.EndsWith(".g.resources"))
					nameToResource[name.Substring(0, name.Length - 12)] = resource;
				int index = name.LastIndexOf('.');
				if (index > 0)
					nameToResource[name.Substring(0, index)] = resource;
			}

			renameResourceNamesInCode(renamedTypes);
			renameResources(renamedTypes);
		}

		void renameResourceNamesInCode(List<TypeInfo> renamedTypes) {
			var oldNameToTypeInfo = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
			foreach (var info in renamedTypes)
				oldNameToTypeInfo[info.oldFullName] = info;

			foreach (var method in module.getAllMethods()) {
				if (!method.HasBody)
					continue;
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode != OpCodes.Ldstr)
						continue;
					var codeString = (string)instr.Operand;
					if (string.IsNullOrEmpty(codeString))
						continue;

					Resource resource;
					if (!nameToResource.TryGetValue(codeString, out resource))
						continue;

					TypeInfo typeInfo;
					if (!oldNameToTypeInfo.TryGetValue(codeString, out typeInfo))
						continue;
					var newName = typeInfo.type.TypeDefinition.FullName;

					bool renameCodeString = module.ObfuscatedFile.RenameResourcesInCode ||
											isCallingResourceManagerCtor(instrs, i, typeInfo);
					if (!renameCodeString)
						Log.v("Possible resource name in code: '{0}' => '{1}' in method {2}", Utils.removeNewlines(codeString), newName, Utils.removeNewlines(method));
					else {
						instr.Operand = newName;
						Log.v("Renamed resource string in code: '{0}' => '{1}' ({2})", Utils.removeNewlines(codeString), newName, Utils.removeNewlines(method));
					}
				}
			}
		}

		static bool isCallingResourceManagerCtor(IList<Instruction> instrs, int ldstrIndex, TypeInfo typeInfo) {
			try {
				int index = ldstrIndex + 1;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					return false;
				if (!MemberReferenceHelper.compareTypes(typeInfo.type.TypeDefinition, ldtoken.Operand as TypeReference))
					return false;

				if (!checkCalledMethod(instrs[index++], "System.Type", "(System.RuntimeTypeHandle)"))
					return false;
				if (!checkCalledMethod(instrs[index++], "System.Reflection.Assembly", "()"))
					return false;

				var newobj = instrs[index++];
				if (newobj.OpCode.Code != Code.Newobj)
					return false;
				if (newobj.Operand.ToString() != "System.Void System.Resources.ResourceManager::.ctor(System.String,System.Reflection.Assembly)")
					return false;

				return true;
			}
			catch (ArgumentOutOfRangeException) {
				return false;
			}
			catch (IndexOutOfRangeException) {
				return false;
			}
		}

		static bool checkCalledMethod(Instruction instr, string returnType, string parameters) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			return DotNetUtils.isMethod(instr.Operand as MethodReference, returnType, parameters);
		}

		class RenameInfo {
			public Resource resource;
			public TypeInfo typeInfo;
			public string newResourceName;
			public RenameInfo(Resource resource, TypeInfo typeInfo, string newResourceName) {
				this.resource = resource;
				this.typeInfo = typeInfo;
				this.newResourceName = newResourceName;
			}
			public override string ToString() {
				return string.Format("{0} => {1}", resource.Name, newResourceName);
			}
		}

		void renameResources(List<TypeInfo> renamedTypes) {
			var newNames = new Dictionary<Resource, RenameInfo>();
			foreach (var info in renamedTypes) {
				var oldFullName = info.oldFullName;
				Resource resource;
				if (!nameToResource.TryGetValue(oldFullName, out resource))
					continue;
				if (newNames.ContainsKey(resource))
					continue;
				var newTypeName = info.type.TypeDefinition.FullName;
				var newName = newTypeName + resource.Name.Substring(oldFullName.Length);
				newNames[resource] = new RenameInfo(resource, info, newName);

				Log.v("Renamed resource in resources: {0} => {1}", Utils.removeNewlines(resource.Name), newName);
				resource.Name = newName;
			}
		}
	}
}
