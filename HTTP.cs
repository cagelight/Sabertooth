using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Sabertooth.Lexicon;

namespace Sabertooth {
	public abstract class HTTPObject : IStreamable{
		public const string Newline = "\r\n";
		public class Instruction {
			public readonly string Key;
			public ITextable Value;
			private List<ITextable> Options = new List<ITextable>();
			public Instruction(string key, ITextable value) {
				Key = key;Value = value;
			}
			public string GetText() {
				string optionsText = String.Empty;
				foreach(ITextable IT in Options) {
					optionsText += " ;" + IT.GetText ();
				}
				return String.Format ("{0}: {1}{2}", Key, Value.GetText(), optionsText);
			}
			public static Instruction ConnectionClose = new Instruction ("Connection", new GenericTextable("close"));
			public static Instruction ConnectionKeepAlive = new Instruction ("Connection", new GenericTextable("keep-alive"));
			internal static Instruction ContentLength(int length) {
				return new Instruction ("Content-Length", new GenericTextable (length));
			}
			internal static Instruction ContentType(MIME type) {
				return new Instruction ("Content-Type", type);
			}
		}
		protected List<Instruction> httpInstructions = new List<Instruction> ();
		public HTTPObject() {

		}

		public abstract byte[] GetBytes ();
		public abstract int GetSize ();
		public abstract void StreamTo (Stream S);
	}

	public class Response : HTTPObject, IStreamable {
		public struct Code : ITextable{
			public readonly int Number; public readonly string Description;
			public Code(int num, string desc) {
				Number=num;Description=desc;
			}
			public string GetText () {
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
		public Response(Code C) {
			httpCode = C;
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
			string returnString = httpCode.GetText () + Newline;
			foreach(Instruction I in httpInstructions) {
				returnString += I.GetText () + Newline;
			}
			return tailingNewline ? returnString + Newline : returnString;
		}

		public string GetFullHeader(bool tailingNewline = false) {
			string nch = GetNoContentHeader ();
			int bodySize = httpBody.GetSize();
			if (bodySize > 0) {
				nch += Instruction.ContentType (httpBody.GetFormat()).GetText() + Newline;
				nch += Instruction.ContentLength (bodySize).GetText() + Newline;
			}
			return tailingNewline ? nch + Newline : nch;
		}

		public override byte[] GetBytes() {
			string responseStr = GetFullHeader (true);
			byte[] headerBytes = Encoding.UTF8.GetBytes (responseStr);
			if(httpBody.GetSize() > 0) {
				byte[] responseBytes = new byte[headerBytes.Length + httpBody.GetSize()];
				Buffer.BlockCopy (headerBytes, 0, responseBytes, 0, headerBytes.Length);
				Buffer.BlockCopy (httpBody.GetBytes(), 0, responseBytes, headerBytes.Length, httpBody.GetSize());
				return responseBytes;
			} else {
				return headerBytes;
			}
		}
		public override int GetSize() {
			return Encoding.UTF8.GetBytes (GetFullHeader (true)).Length + httpBody.GetSize ();
		}
		public override void StreamTo(Stream S) {
			MemoryStream MS = new MemoryStream (this.GetBytes ());
			MS.CopyTo (S);
			MS.Flush();
		}
		public static Response Standard100 { get{
				Response R = new Response (Code.N100);
				R.AddInstruction (Instruction.ConnectionKeepAlive);
				return R;
			}
		}
		public static Response Standard400 { get{
				Response R = new Response (Code.N400);
				R.AddInstruction (Instruction.ConnectionKeepAlive);
				return R;
			}
		}
		public static Response Standard500 { get{
				Response R = new Response (Code.N500);
				R.AddInstruction (Instruction.ConnectionClose);
				return R;
			}
		}
		public static Response Standard501 { get{
				Response R = new Response (Code.N501);
				R.AddInstruction (Instruction.ConnectionKeepAlive);
				return R;
			}
		}
	}

	public class HEADResponse : Response, IStreamable {
		public HEADResponse(Response.Code C) : base(C) {}
		public override byte[] GetBytes () {
			return Encoding.UTF8.GetBytes (GetFullHeader (true));
		}
	}
}

