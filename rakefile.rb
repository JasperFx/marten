require 'json'

COMPILE_TARGET = ENV['config'].nil? ? "debug" : ENV['config']
RESULTS_DIR = "results"
BUILD_VERSION = '1.5.2'
CONNECTION = ENV['connection']

tc_build_number = ENV["BUILD_NUMBER"]
build_revision = tc_build_number || Time.new.strftime('5%H%M')
build_number = "1.5.3.#{build_revision}"
BUILD_NUMBER = build_number

task :ci => [:connection, :version, :default, 'pack']

task :default => [:mocha, :test]

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
      :copyright => 'Copyright 2016-17 Jeremy D. Miller et al. All rights reserved.',
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
    file.write "[assembly: AssemblyVersion(\"#{build_number}\")]\n"
    file.write "[assembly: AssemblyFileVersion(\"#{options[:file_version]}\")]\n"
    file.write "[assembly: AssemblyInformationalVersion(\"#{options[:informational_version]}\")]\n"
  end

  puts 'Writing version to project.json'
  nuget_version = "#{BUILD_VERSION}"
  project_file = load_project_file('src/Marten/project.json')
  File.open('src/Marten/project.json', "w+") do |file|
    project_file["version"] = nuget_version
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
task :compile => [:clean, :restore] do
  sh "dotnet build ./src/Marten.Testing/ --configuration #{COMPILE_TARGET}"
end

desc 'Run the unit tests'
task :test => [:compile] do
  sh 'dotnet test src/Marten.Testing --framework netcoreapp1.0'
end


desc "Launches VS to the Marten solution file"
task :sln do
  sh "start src/Marten.sln"
end

desc "Run the storyteller specifications"
task :storyteller => [:compile] do
	Dir.chdir("src/Marten.Storyteller") do
	  system "dotnet storyteller run -r artifacts --culture en-US"
	end
end

desc "Run the storyteller specifications"
task :open_st => [:compile] do
	Dir.chdir("src/Marten.Storyteller") do
	  system "dotnet storyteller open --culture en-US"
	end
end

"Launches the documentation project in editable mode"
task :docs do
	sh "dotnet restore"
	sh "dotnet stdocs run -v #{BUILD_VERSION}"
end

"Exports the documentation to structuremap.github.io - requires Git access to that repo though!"
task :publish do
	FileUtils.remove_dir('doc-target') if Dir.exists?('doc-target')

	if !Dir.exists? 'doc-target' 
		Dir.mkdir 'doc-target'
		sh "git clone -b gh-pages https://github.com/jasperfx/marten.git doc-target"
	else
		Dir.chdir "doc-target" do
			sh "git checkout --force"
			sh "git clean -xfd"
			sh "git pull origin master"
		end
	end
	
	sh "dotnet restore"
	sh "dotnet stdocs export doc-target ProjectWebsite --version #{BUILD_VERSION} --project marten"
	
	Dir.chdir "doc-target" do
		sh "git add --all"
		sh "git commit -a -m \"Documentation Update for #{BUILD_VERSION}\" --allow-empty"
		sh "git push origin gh-pages"
	end
	

	

end

desc 'Restores nuget packages'
task :restore do
    sh 'dotnet restore src'
end

desc 'Run Benchmarks'
task :benchmarks => [:restore] do
	sh 'dotnet run --project src/MartenBenchmarks --configuration Release'
end


desc 'Build the Nupkg file'
task :pack => [:compile] do
	sh "dotnet pack ./src/Marten -o artifacts --configuration Release"
	sh "dotnet pack ./src/Marten.CommandLine -o artifacts --configuration Release"
end


def load_project_file(project)
  File.open(project) do |file|
    file_contents = File.read(file, :encoding => 'bom|utf-8')
    JSON.parse(file_contents)
  end
end
