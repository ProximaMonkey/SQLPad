﻿using System;
using System.Configuration;

namespace SqlPad
{
	public class ConfigurationProvider
	{
		private static readonly IInfrastructureFactory InternalInfrastructureFactory = (IInfrastructureFactory)Activator.CreateInstance(Type.GetType("SqlPad.Oracle.OracleInfrastructureFactory, SqlPad.Oracle"));

		public static IInfrastructureFactory InfrastructureFactory { get { return InternalInfrastructureFactory; } }

		public static ConnectionStringSettingsCollection ConnectionStrings { get { return ConfigurationManager.ConnectionStrings; } }
	}
}