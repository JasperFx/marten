require 'json'

COMPILE_TARGET = ENV['config'].nil? ? "debug" : ENV['config']
RESULTS_DIR = "results"
BUILD_VERSION = '1.0.0-alpha';
CONNECTION = ENV['connection']

tc_build_number = ENV["BUILD_NUMBER"]
build_revision = tc_build_number || Time.new.strftime('5%H%M')
build_number = "#{BUILD_VERSION}-#{build_revision}"
BUILD_NUMBER = build_number

task :ci => [:connection, :version, :default, 'nuget:pack']

task :default => [:mocha, :test, :storyteller]

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
      :product_name => 'Marten',
      :copyright => 'Copyright 2016 Jeremy D. Miller et al. All rights reserved.',
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

  puts 'Writing version to project.json'
  project_file = load_project_file('src/Marten/project.json')
  File.open('src/Marten/project.json', "r+") do |file|
    project_file["version"] = options[:version]
    file.write(JSON.pretty_generate project_file)
  end
end

desc 'Builds the connection string file'
task :connection do
  File.open('src/Marten.Testing/connection.txt', 'w') do |file|
    file.write CONNECTION
  end
end

desc 'Runs the Mocha tests'
task :mocha do
  sh "npm install"
  sh "npm run test"
end

desc 'Compile the code'
task :compile => [:clean, 'nuget:restore'] do
  msbuild = '"C:\Program Files (x86)\MSBuild\14.0\Bin\msbuild.exe"'

  #Eventually this part won't be required, but ensures things compile for both old and new
  sh "#{msbuild} src/Marten.sln   /property:Configuration=#{COMPILE_TARGET} /v:m /t:rebuild /nr:False /maxcpucount:2"

  #sh "ILMerge.exe /out:src/Marten/bin/#{COMPILE_TARGET}/Marten.dll /lib:src/Marten/bin/#{COMPILE_TARGET} /target:library /targetplatform:v4 /internalize /ndebug src/Marten/bin/#{COMPILE_TARGET}/Marten.dll src/Marten/bin/#{COMPILE_TARGET}/Newtonsoft.Json.dll src/Marten/bin/#{COMPILE_TARGET}/Baseline.dll  src/Marten/bin/#{COMPILE_TARGET}/Remotion.Linq.dll"

  sh 'dotnet build ./src/Marten.Testing/'
end

desc 'Run the unit tests'
task :test => [:compile] do
  Dir.mkdir RESULTS_DIR

  #sh "packages/xunit.runner.console/tools/xunit.console.exe src/Marten.Testing/bin/#{COMPILE_TARGET}/Marten.Testing.dll -html results/xunit.htm"
  cd './src/Marten.Testing'
  sh 'dotnet test'
  cd '../../'

  #puts "Running the unit tests under the '9.5 Upsert' mode"
  #sh "packages/Fixie/lib/net45/Fixie.Console.exe src/Marten.Testing/bin/#{COMPILE_TARGET}/Marten.Testing.dll --NUnitXml results/TestResult.xml --upsert Standard"
end


desc "Launches VS to the Marten solution file"
task :sln do
  sh "start src/Marten.sln"
end

desc "Run the storyteller specifications"
task :storyteller => [:compile] do
  storyteller_cmd = storyteller_path()
  sh "#{storyteller_cmd} run src/Marten.Testing --results-path artifacts/stresults.htm --build #{COMPILE_TARGET}"
end

desc "Run the storyteller specifications"
task :open_st => [:compile] do
  storyteller_cmd = storyteller_path()
  sh "#{storyteller_cmd} open src/Marten.Testing"
end

desc "Launches the documentation project in editable mode"
task :docs => ['nuget:restore'] do
  storyteller_cmd = storyteller_path()
  sh "#{storyteller_cmd} doc-run -v #{BUILD_VERSION}"
end

namespace :nuget do
  desc 'Pulls the latest paket.exe into .paket folder'
  task :bootstrap do
    #sh '.paket/paket.bootstrapper.exe' unless File.exists? '.paket/paket.exe'
  end

  desc 'Restores nuget packages with paket'
  task :restore => [:bootstrap] do
    sh 'dotnet restore'
    sh './nuget.exe restore ./src/Marten.sln'
    #sh '.paket/paket.exe restore'
  end

  desc 'Setup paket.exe symlink for convenience (requires elevation)'
  task :symlink do
    #sh '.paket/paket.bootstrapper.exe'
    #sh 'cmd.exe /c mklink .\paket.exe .paket\paket.exe'
  end

  desc 'Build the Nupkg file'
  task :pack => [:compile] do
    sh "dotnet pack ./src/Marten -o artifacts"
    #sh ".paket/paket.exe pack output artifacts version #{BUILD_NUMBER}"
  end
end

def storyteller_path()
  global_cache = `./nuget.exe locals global-packages -list`
  global_cache = global_cache.split(': ').last.strip
  project_file = load_project_file("src/Marten.Testing/project.json")
  storyteller_version = project_file["frameworks"]["net46"]["dependencies"]["Storyteller"]
  "#{global_cache}Storyteller/#{storyteller_version}/tools/st.exe"
end

def load_project_file(project)
  File.open(project) do |file|
    file_contents = File.read(file, :encoding => 'bom|utf-8')
    JSON.parse(file_contents)
  end
end
