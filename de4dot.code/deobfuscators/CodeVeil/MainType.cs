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
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	// Detects the type CV adds to the assembly that gets called from <Module>::.cctor.
	class MainType {
		ModuleDefinition module;
		TypeDefinition theType;
		MethodDefinition initMethod;
		MethodDefinition tamperCheckMethod;
		ObfuscatorVersion obfuscatorVersion = ObfuscatorVersion.Unknown;
		List<int> rvas = new List<int>();	// _stub and _executive
		List<MethodDefinition> otherInitMethods = new List<MethodDefinition>();

		public bool Detected {
			get { return theType != null; }
		}

		public ObfuscatorVersion Version {
			get { return obfuscatorVersion; }
		}

		public TypeDefinition Type {
			get { return theType; }
		}

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public List<MethodDefinition> OtherInitMethods {
			get { return otherInitMethods; }
		}

		public MethodDefinition TamperCheckMethod {
			get { return tamperCheckMethod; }
		}

		public List<int> Rvas {
			get { return rvas; }
		}

		public MainType(ModuleDefinition module) {
			this.module = module;
		}

		public MainType(ModuleDefinition module, MainType oldOne) {
			this.module = module;
			this.theType = lookup(oldOne.theType, "Could not find main type");
			this.initMethod = lookup(oldOne.initMethod, "Could not find main type init method");
			this.tamperCheckMethod = lookup(oldOne.tamperCheckMethod, "Could not find tamper detection method");
			this.obfuscatorVersion = oldOne.obfuscatorVersion;
			this.rvas = oldOne.rvas;
			foreach (var otherInitMethod in otherInitMethods)
				otherInitMethods.Add(lookup(otherInitMethod, "Could not find otherInitMethod"));
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;

			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;

				var ldci4_2 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var initMethodTmp = call.Operand as MethodDefinition;
				ObfuscatorVersion obfuscatorVersionTmp;
				if (!checkInitMethod(initMethodTmp, out obfuscatorVersionTmp))
					continue;
				if (!checkMethodsType(initMethodTmp.DeclaringType))
					continue;

				obfuscatorVersion = obfuscatorVersionTmp;
				theType = initMethodTmp.DeclaringType;
				initMethod = initMethodTmp;
				break;
			}
		}

		static string[] fieldTypesV5 = new string[] {
			"System.Byte[]",
			"System.Collections.Generic.List`1<System.Delegate>",
			"System.Runtime.InteropServices.GCHandle",
		};
		bool checkInitMethod(MethodDefinition initMethod, out ObfuscatorVersion obfuscatorVersionTmp) {
			obfuscatorVersionTmp = ObfuscatorVersion.Unknown;

			if (initMethod == null)
				return false;
			if (initMethod.Body == null)
				return false;
			if (!initMethod.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(initMethod, "System.Void", "(System.Boolean,System.Boolean)"))
				return false;

			if (hasCodeString(initMethod, "E_FullTrust")) {
				if (DotNetUtils.getPInvokeMethod(initMethod.DeclaringType, "user32", "CallWindowProcW") != null)
					obfuscatorVersionTmp = ObfuscatorVersion.V4_1;
				else
					obfuscatorVersionTmp = ObfuscatorVersion.V4_0;
			}
			else if (hasCodeString(initMethod, "Full Trust Required"))
				obfuscatorVersionTmp = ObfuscatorVersion.V3;
			else if (initMethod.DeclaringType.HasNestedTypes && new FieldTypes(initMethod.DeclaringType).all(fieldTypesV5))
				obfuscatorVersionTmp = ObfuscatorVersion.V5_0;
			else
				return false;

			return true;
		}

		static bool hasCodeString(MethodDefinition method, string str) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				if (s == str)
					return true;
			}
			return false;
		}

		bool checkMethodsType(TypeDefinition type) {
			rvas = new List<int>();

			var fields = getRvaFields(type);
			if (fields.Count < 2)	// RVAs for executive and stub are always present if encrypted methods
				return true;

			foreach (var field in fields)
				rvas.Add(field.RVA);
			return true;
		}

		static List<FieldDefinition> getRvaFields(TypeDefinition type) {
			var fields = new List<FieldDefinition>();
			foreach (var field in type.Fields) {
				if (field.FieldType.EType != ElementType.U1 && field.FieldType.EType != ElementType.U4)
					continue;
				if (field.RVA == 0)
					continue;

				fields.Add(field);
			}
			return fields;
		}

		public void initialize() {
			if (theType == null)
				return;

			tamperCheckMethod = findTamperCheckMethod();
			otherInitMethods = findOtherInitMethods();
		}

		MethodDefinition findTamperCheckMethod() {
			foreach (var method in theType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.Reflection.Assembly,System.UInt64)"))
					continue;

				return method;
			}

			return null;
		}

		List<MethodDefinition> findOtherInitMethods() {
			var list = new List<MethodDefinition>();
			foreach (var method in theType.Methods) {
				if (!method.IsStatic)
					continue;
				if (method.Name == ".cctor")
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;

				list.Add(method);
			}
			return list;
		}

		public MethodDefinition getInitStringDecrypterMethod(MethodDefinition stringDecrypterInitMethod) {
			if (stringDecrypterInitMethod == null)
				return null;
			if (theType == null)
				return null;

			foreach (var method in theType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (callsMethod(method, stringDecrypterInitMethod))
					return method;
			}
			return null;
		}

		bool callsMethod(MethodDefinition methodToCheck, MethodDefinition calledMethod) {
			foreach (var info in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				if (info.Item2 == calledMethod)
					return true;
			}
			return false;
		}

		public void removeInitCall(Blocks blocks) {
			if (initMethod == null || theType == null)
				return;
			if (blocks.Method.Name != ".cctor")
				return;
			if (blocks.Method.DeclaringType != DotNetUtils.getModuleType(module))
				return;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					if (!instrs[i].isLdcI4())
						continue;
					if (!instrs[i + 1].isLdcI4())
						continue;
					var call = instrs[i + 2];
					if (call.OpCode.Code != Code.Call)
						continue;
					if (call.Operand != initMethod)
						continue;

					block.remove(i, 3);
					return;
				}
			}
		}
	}
}
