require 'bundler/setup'

require 'albacore'
require 'albacore/tasks/versionizer'
require 'albacore/ext/teamcity'

Configuration = ENV['CONFIGURATION'] || 'Release'

Albacore::Tasks::Versionizer.new :versioning

desc 'create assembly infos'
asmver_files :assembly_info do |a|
  a.files = FileList['**/*proj'] # optional, will find all projects recursively by default
  a.attributes assembly_description: 'TODO',
               assembly_configuration: Configuration,
               assembly_company: 'Foretag AB',
               assembly_copyright: "(c) 2016 by John Doe",
               assembly_version: ENV['LONG_VERSION'],
               assembly_file_version: ENV['LONG_VERSION'],
               assembly_informational_version: ENV['BUILD_VERSION']
end

desc 'Perform fast build (warn: doesn\'t d/l deps)'
build :quick_compile do |b|
  b.prop 'Configuration', Configuration
  b.logging = 'detailed'
  b.sln     = 'SmbPingPong/SmbPingPong.sln'
end

task :yolo do
   sh %{ruby -pi.bak -e "gsub(/module internal YoLo/, 'module internal SmbPingPong.YoLo')" paket-files/haf/YoLo/YoLo.fs}
end

task :paket_bootstrap do
  system 'tools/paket.bootstrapper.exe', clr_command: true unless   File.exists? 'tools/paket.exe'
end

desc 'restore all nugets as per the packages.config files'
task :restore => [:paket_bootstrap, :yolo] do
  system 'tools/paket.exe', 'restore', clr_command: true
end

desc 'Perform full build'
build :compile => [:versioning, :restore, :assembly_info] do |b|
  b.prop 'Configuration', Configuration
  b.sln = 'SmbPingPong/SmbPingPong.sln'
end

task :default => :compile
