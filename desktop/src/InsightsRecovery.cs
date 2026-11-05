using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace CreditMonitor.Insights {
 public static class InsightRecovery {
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close_v2(IntPtr db);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int size,out IntPtr stmt,IntPtr tail);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr stmt);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr stmt);
  [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr stmt,int index);
  static byte[] Utf8(string text){return Encoding.UTF8.GetBytes(text+"\0");}
  // Read only; an access/locking/newer-schema failure must never be called corruption.
  public static bool IsCorrupt(string path){if(!File.Exists(path))return false;IntPtr db=IntPtr.Zero,stmt=IntPtr.Zero;try{int rc=sqlite3_open_v2(Utf8(path),out db,1|0x10000,IntPtr.Zero);if((rc&255)==11||(rc&255)==26)return true;if(rc!=0)return false;rc=sqlite3_prepare_v2(db,Utf8("PRAGMA quick_check"),-1,out stmt,IntPtr.Zero);if((rc&255)==11||(rc&255)==26)return true;if(rc!=0)return false;rc=sqlite3_step(stmt);if((rc&255)==11||(rc&255)==26)return true;return rc==100&&Marshal.PtrToStringAnsi(sqlite3_column_text(stmt,0))!="ok";}catch{return false;}finally{if(stmt!=IntPtr.Zero)sqlite3_finalize(stmt);if(db!=IntPtr.Zero)sqlite3_close_v2(db);}}
  // Call only after explicit user confirmation. Never delete or overwrite any history bytes.
  public static string Preserve(string path){path=Path.GetFullPath(path);if(!File.Exists(path))throw new IOException("History file unavailable");string backup=Path.Combine(Path.GetDirectoryName(path),"history-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8));var held=new List<FileStream>();var originals=new List<string>();var moved=new List<string>();try{foreach(string suffix in new[]{"","-wal","-shm","-journal"}){string file=path+suffix;if(!File.Exists(file))continue;held.Add(new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Delete));originals.Add(file);}Directory.CreateDirectory(backup);foreach(string file in originals){File.Move(file,Path.Combine(backup,Path.GetFileName(file)));moved.Add(file);}return backup;}catch{for(int i=moved.Count-1;i>=0;i--)try{if(!File.Exists(moved[i]))File.Move(Path.Combine(backup,Path.GetFileName(moved[i])),moved[i]);}catch{/* Keep the remaining bytes in the uniquely named backup. */}throw;}finally{foreach(var stream in held)stream.Dispose();}}
 }
}
