# Users
Users is saved in [user repositories](#user-repository) where each track contains exactly one user repository. Users are authenticated using a [up-party login](login.md) user interface (UI).

## User configuration
New users can be created by the administrator through [FoxIDs Control Client](control.md#foxids-control-client) or be provisioned through [FoxIDs Control API](control.md#foxids-control-api).

![Configure Login](images/configure-user.png)

## User repository 
Each track contains a user repository supporting an unlimited number of users because they are saved in Cosmos DB. The users id, email and other claims are saved as text.  
The password is never saved needer in logs or in Cosmos DB. Instead, a hash of the password is saved along with the rest of the user information.

### Password hash
FoxIDs is designed to support a growing number of algorithms with different iterations by saving information about the hash algorithm used alongside the actually hash. Therefore, FoxIDs can validate an old hash algorithm and at the same time save new hashes with a new hash algorithm.

Currently FoxIDs support and use hash algorithm `P2HS512:10` which is defined as:

- The `HMAC` algorithm (`RFC 2104`) using the `SHA-512` hash function (`FIPS 180-4`).
- With 10 iterations.
- Salt is generated from 64 bytes.
- Derived key length is 80 bytes.

Standard .NET liberals are used to calculate the hash.
