using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Sabertooth.Lexicon;
using WebSharp;

namespace Sabertooth.HTTP {
	public abstract class HTTPObject : IStreamable{
		public const string Newline = "\r\n";
		protected List<Instruction> httpInstructions = new List<Instruction> ();
		public bool IsLoaded{ get{return true;}}
		public HTTPObject() {

		}

		public abstract byte[] GetBytes ();
		public abstract int GetSize ();
		public abstract void StreamTo (Stream S);
	}
	public class Instruction {
		public readonly string Key;
		public string Value;
		private List<string> Options = new List<string>();
		public Instruction(string key, string value) {
			Key = key;Value = value;
		}
		public string GetText() {
			string optionsText = String.Empty;
			foreach(string IT in Options) {
				optionsText += " ;" + IT;
			}
			return String.Format ("{0}: {1}{2}", Key, Value, optionsText);
		}
		public enum Connection {KeepAlive, Close}
		internal static Instruction ConnectionClose = new Instruction ("Connection", "close");
		internal static Instruction ConnectionKeepAlive = new Instruction ("Connection", "keep-alive");
		internal static Instruction ContentLength(int length) {
			return new Instruction ("Content-Length", length.ToString());
		}
		internal static Instruction ContentType(MIME type) {
			return new Instruction ("Content-Type", type.ToString());
		}
	}
	public class Response : HTTPObject, IStreamable {
		public struct Code {
			public readonly int Number; public readonly string Description;
			public Code(int num, string desc) {
				Number=num;Description=desc;
			}
			public override string ToString () {
				return "HTTP/1.1 "+Number+" "+Description;
			}
			public static readonly Code N100 = new Code(100, "Continue");
			public static readonly Code N200 = new Code(200, "OK");
			public static readonly Code N400 = new Code(400, "Bad Request");
			public static readonly Code N403 = new Code(403, "Forbidden");
			public static readonly Code N404 = new Code(404, "Not Found");
			public static readonly Code N500 = new Code(500, "Internal Server Error");
			public static readonly Code N501 = new Code(501, "Not Implemented");
		}
		Code httpCode;
		IStreamableContent httpBody = new GeneratedResource (new byte[]{}, MIME.Plaintext);

		public Instruction.Connection connectionStatus = Instruction.Connection.KeepAlive;
		public Response(Code C, IStreamableContent body) {
			httpCode = C;
			this.httpBody = body;
		}

		public void AddInstruction(Instruction I) {
			this.httpInstructions.Add (I);
		}

		public void SetBody(byte[] B, MIME M) {
			this.httpBody = new GeneratedResource (B, M);
		}
		public void SetBody(IStreamableContent ICS) {
			this.httpBody = ICS;
		}

		public string GetNoContentHeader(bool tailingNewline = false) {
			string returnString = httpCode.ToString() + Newline;
			returnString += (connectionStatus == Instruction.Connection.KeepAlive ? Instruction.ConnectionKeepAlive.GetText() : Instruction.ConnectionClose.GetText()) + Newline;
			foreach(Instruction I in httpInstructions) {
				returnString += I.GetText () + Newline;
			}
			return tailingNewline ? returnString + Newline : returnString;
		}

		public override byte[] GetBytes() {
			string responseStr = GetNoContentHeader (false);
			byte[] bodybytes = httpBody.GetBytes ();
			if(bodybytes.Length > 0) {
				responseStr += Instruction.ContentType (httpBody.Format).GetText () + Newline;
				responseStr += Instruction.ContentLength (bodybytes.Length).GetText() + Newline;
			}
			byte[] headerBytes = Encoding.UTF8.GetBytes (responseStr + Newline);
			if(bodybytes.Length > 0) {
				byte[] responseBytes = new byte[headerBytes.Length + httpBody.GetSize()];
				Buffer.BlockCopy (headerBytes, 0, responseBytes, 0, headerBytes.Length);
				Buffer.BlockCopy (httpBody.GetBytes(), 0, responseBytes, headerBytes.Length, httpBody.GetSize());
				return responseBytes;
			} else {
				return headerBytes;
			}
		}
		public override int GetSize() {
			return -1;
		}
		public override void StreamTo(Stream S) {
			if (httpBody != null) {
				if (httpBody.IsLoaded) {
					byte[] response = this.GetBytes ();
					MemoryStream MS = new MemoryStream (response);
					MS.CopyTo (S);
					MS.Flush ();
					//MS.Close ();
				} else {
					string nchead = this.GetNoContentHeader (false);
					nchead += Instruction.ContentType (httpBody.Format).GetText () + Newline;
					int bodylength = httpBody.GetSize ();
					if (bodylength > -1) {
						nchead += Instruction.ContentLength (bodylength).GetText () + Newline;
					}
					byte[] headerbytes = Encoding.UTF8.GetBytes (nchead + Newline);
					S.Write (headerbytes, 0, headerbytes.Length);
					httpBody.StreamTo (S);
				}
			} else {
				MemoryStream MS = new MemoryStream (Encoding.UTF8.GetBytes(this.GetNoContentHeader(true)));
				MS.CopyTo (S);
				MS.Flush ();
				//MS.Close ();
			}
		}
	}

	public class HEADResponse : Response, IStreamable {
		public HEADResponse(Response.Code C) : base(C, new TextResource("")) {}
		public override byte[] GetBytes () {
			return Encoding.UTF8.GetBytes (GetNoContentHeader (true));
		}
	}
}

