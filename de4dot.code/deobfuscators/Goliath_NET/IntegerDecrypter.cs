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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class IntegerDecrypter : DecrypterBase {
		public IntegerDecrypter(ModuleDefinition module)
			: base(module) {
		}

		static string[] requiredFields = new string[] {
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.Int32,System.Object>",
		};
		protected override bool checkDecrypterType(TypeDefinition type) {
			return new FieldTypes(type).exactly(requiredFields);
		}

		protected override bool checkDelegateInvokeMethod(MethodDefinition invokeMethod) {
			return DotNetUtils.isMethod(invokeMethod, "System.Object", "(System.Int32)");
		}

		public int decrypt(MethodDefinition method) {
			var info = getInfo(method);
			decryptedReader.BaseStream.Position = info.offset;
			int len = decryptedReader.ReadInt32();
			return BitConverter.ToInt32(decryptedReader.ReadBytes(len), 0);
		}
	}
}
