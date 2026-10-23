// What differs between native platforms and the web (spec 0004 PLT-1, NET-6):
// the native file may import dart:io, the web one never does.
export 'platform_web.dart' if (dart.library.io) 'platform_io.dart';
