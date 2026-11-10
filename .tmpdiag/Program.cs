using System;
using System.IO;
using Microsoft.Data.Sqlite;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
var root = Path.Combine(Path.GetTempPath(), "jm2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
string JM(string p){ using var c=new SqliteConnection($"Data Source={p}"); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText="PRAGMA journal_mode;"; return cmd.ExecuteScalar()!.ToString()!; }
var a=Path.Combine(root,"a.db"); using(var c=new SqliteConnection($"Data Source={a}")){c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText="CREATE TABLE T(Id INTEGER);"; cmd.ExecuteNonQuery();} Console.WriteLine("after CREATE TABLE="+JM(a));
var b=Path.Combine(root,"b.db"); var o=new DbContextOptionsBuilder<DraftEngineDbContext>().UseSqlite($"Data Source={b}").Options; using(var ctx=new DraftEngineDbContext(o)) await ctx.Database.MigrateAsync(); Console.WriteLine("after Migrate="+JM(b));
