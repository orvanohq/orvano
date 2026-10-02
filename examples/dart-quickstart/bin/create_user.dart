import 'dart:io';

import 'package:dart_quickstart/orvano.dart';
import 'package:orvano_dart/orvano_dart.dart';

// #region create-user
/// Creates a user with the API key:
/// `dart run bin/create_user.dart <email> <password> [name]`.
Future<void> main(List<String> args) async {
  if (args.length < 2) {
    stderr.writeln(
      'Usage: dart run bin/create_user.dart <email> <password> [name]',
    );
    exitCode = 64;
    return;
  }

  try {
    final user = await orvano.users.create(
      CreateUserRequest(
        email: args[0],
        password: args[1],
        name: args.length > 2 ? args[2] : null,
      ),
    );
    print('Created user ${user.id} (${user.email}).');
  } on OrvanoException catch (error) {
    stderr.writeln('Orvano refused: ${error.message} (${error.code})');
    exitCode = 1;
  } finally {
    client.close();
  }
}

// #endregion create-user
