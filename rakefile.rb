COMPILE_TARGET = ENV['config'].nil? ? "debug" : ENV['config']
RESULTS_DIR = "results"
BUILD_VERSION = '0.0.1'

tc_build_number = ENV["BUILD_NUMBER"]
build_revision = tc_build_number || Time.new.strftime('5%H%M')
build_number = "#{BUILD_VERSION}.#{build_revision}"
BUILD_NUMBER = build_number 

task :ci => [:default, :pack]

task :default => [:test]

desc "Prepares the working directory for a new build"
task :clean do
	#TODO: do any other tasks required to clean/prepare the working directory
	FileUtils.rm_rf RESULTS_DIR
	FileUtils.rm_rf 'artifacts'

end

desc "Update the version information for the build"
task :version do
  asm_version = build_number
  
  begin
    commit = `git log -1 --pretty=format:%H`
  rescue
    commit = "git unavailable"
  end
  puts "##teamcity[buildNumber '#{build_number}']" unless tc_build_number.nil?
  puts "Version: #{build_number}" if tc_build_number.nil?
  
  options = {
	:description => 'Postgresql as a Document Db and Event Store for .Net Development',
	:product_name => 'StructureMap',
	:copyright => 'Copyright 2015 Jeremy D. Miller et al. All rights reserved.',
	:trademark => commit,
	:version => asm_version,
	:file_version => build_number,
	:informational_version => asm_version
	
  }
  
  puts "Writing src/CommonAssemblyInfo.cs..."
	File.open('src/CommonAssemblyInfo.cs', 'w') do |file|
		file.write "using System.Reflection;\n"
		file.write "using System.Runtime.InteropServices;\n"
		file.write "[assembly: AssemblyDescription(\"#{options[:description]}\")]\n"
		file.write "[assembly: AssemblyProduct(\"#{options[:product_name]}\")]\n"
		file.write "[assembly: AssemblyCopyright(\"#{options[:copyright]}\")]\n"
		file.write "[assembly: AssemblyTrademark(\"#{options[:trademark]}\")]\n"
		file.write "[assembly: AssemblyVersion(\"#{options[:version]}\")]\n"
		file.write "[assembly: AssemblyFileVersion(\"#{options[:file_version]}\")]\n"
		file.write "[assembly: AssemblyInformationalVersion(\"#{options[:informational_version]}\")]\n"
	end
end

desc 'Compile the code'
task :compile => [:clean, :version] do
	sh "C:/Windows/Microsoft.NET/Framework/v4.0.30319/msbuild.exe src/Marten.sln   /property:Configuration=#{COMPILE_TARGET} /v:m /t:rebuild /nr:False /maxcpucount:2"
end

desc 'Run the unit tests'
task :test => [:compile] do
	Dir.mkdir RESULTS_DIR

	sh "packages/Fixie/lib/net45/Fixie.Console.exe src/Marten.Testing/bin/#{COMPILE_TARGET}/Marten.Testing.dll --NUnitXml results/TestResult.xml"
end


desc "Launches VS to the Marten solution file"
task :sln do
	sh "start src/Marten.sln"
end

