# Requirements Document

## Introduction

This feature introduces a two-phase staff onboarding workflow to the existing ASP.NET Core (.NET 9) HR system. In the first phase, a Person is registered with full personal details and automatically receives a system Login ID and password, giving them identity in the system before any position is assigned. In the second phase, a registered Person is hired into an existing vacant Vacancy, linking them to the organisation tree and marking the position as filled. This replaces the current `POST /api/staff/hire/{vacancyId}` flow, which creates a minimal Staff record inline without prior registration.

## Glossary

- **Person**: A new entity that stores the full personal profile of an individual. A Person exists independently of any Vacancy and may be in an "unassigned" state.
- **Staff**: The existing entity that records the employment relationship between a Person and a Vacancy. After hire, a Staff record is created (or updated) to link the Person to the Vacancy.
- **Vacancy**: An existing entity representing an open position within a branch of the OrganizationTree. A Vacancy has `IsFilled = false` when no Person is assigned.
- **Login_ID**: A system-generated unique identifier in the format `{CompanyPrefix}-{IncrementalNumber}` (e.g. `LT-10001`). Used as the ASP.NET Identity `UserName` for the Person.
- **CompanyPrefix**: A short alphabetic code derived from the Company node in the OrganizationTree (e.g. `LT`). Stored on the Company node's `Code` field.
- **IncrementalNumber**: A zero-padded 5-digit integer that increments from the highest number already issued for a given CompanyPrefix (e.g. 10001, 10002, …).
- **Registration_Service**: The application service responsible for creating a Person record, generating a Login_ID, and creating the corresponding ASP.NET Identity user.
- **Hire_Service**: The application service responsible for linking a Person to a Vacancy, creating the Staff record, and marking the Vacancy as filled.
- **Person_Controller**: The API controller that exposes person management endpoints under `/api/persons`.
- **Staff_Controller**: The existing API controller under `/api/staff`, extended with the new hire-person endpoint.
- **Address**: A value object containing: AddressLine, Country, Province, District, City, and PostalCode.
- **Unassigned_Person**: A Person who has no associated Staff record with a non-null VacancyId — i.e. not yet hired into any position.
- **ApplicationDbContext**: The existing EF Core context class (`ApplicationDbContext`) that manages all database entities.
- **IsHired**: A computed boolean on the Person response — `true` when a Staff record with a non-null VacancyId exists for the Person, `false` otherwise.

---

## Requirements

### Requirement 1: Person Entity and Data Model

**User Story:** As a Manager, I want a dedicated Person entity with full personal details, so that individuals can be registered in the system before being assigned to a position.

#### Acceptance Criteria

1. THE **ApplicationDbContext** SHALL persist a `Persons` table containing the following fields: `PersonId` (GUID, primary key, default `NEWID()`), `FullName` (max 150 chars, required), `Phone` (max 50 chars, optional), `Email` (max 150 chars, optional), `Gender` (max 20 chars, optional), `DateOfBirth` (date, optional), `MaritalStatus` (max 50 chars, optional), `ProfilePhotoUrl` (max 500 chars, optional), `LoginId` (max 30 chars, unique, required), `IdentityUserId` (max 450 chars, FK to `AspNetUsers.Id`, required), `CreatedDate` (datetime, default `GETDATE()`).
2. THE **ApplicationDbContext** SHALL persist a `PersonAddresses` table containing: `AddressId` (GUID, primary key, default `NEWID()`), `PersonId` (GUID, FK to `Persons`), `AddressType` (max 20 chars — value must be exactly `Current` or `Permanent`, enforced at the application layer), `AddressLine` (max 250 chars, optional), `Country` (max 100 chars, optional), `Province` (max 100 chars, optional), `District` (max 100 chars, optional), `City` (max 100 chars, optional), `PostalCode` (max 20 chars, optional).
3. THE **ApplicationDbContext** SHALL enforce a unique index on `Persons.LoginId`.
4. THE **ApplicationDbContext** SHALL enforce a unique index on `Persons.IdentityUserId`.
5. THE **ApplicationDbContext** SHALL enforce a unique composite index on `(PersonAddresses.PersonId, PersonAddresses.AddressType)` so that each Person has at most one `Current` address and at most one `Permanent` address.
6. WHEN a Person is deleted, THE **ApplicationDbContext** SHALL cascade-delete the associated `PersonAddresses` rows.
7. THE **ApplicationDbContext** SHALL configure the FK from `Persons.IdentityUserId` to `AspNetUsers.Id` with `DeleteBehavior.Restrict` so that deleting an Identity user is blocked while a Person record references it.
8. THE **Staff** entity SHALL include a nullable `PersonId` (GUID, FK to `Persons`) configured with `DeleteBehavior.SetNull`, consistent with the existing `VacancyId` SetNull pattern, so that deleting a Person sets `Staff.PersonId` to null rather than deleting the Staff record.

---

### Requirement 2: Login ID Generation

**User Story:** As a Manager, I want the system to auto-generate a unique Login ID for each registered Person, so that every individual has a consistent, human-readable identifier.

#### Acceptance Criteria

1. WHEN a Person registration is requested, THE **Registration_Service** SHALL generate a Login_ID in the format `{CompanyPrefix}-{IncrementalNumber}`, where `CompanyPrefix` is the `Code` field of the Company node identified by the `CompanyId` supplied in the registration request, and `IncrementalNumber` is a zero-padded 5-digit integer (e.g. `10001`).
2. IF the Company node identified by `CompanyId` has a null or empty `Code` field, THEN THE **Registration_Service** SHALL return HTTP 400 with the message `"Company node {CompanyId} has no code set. A company code is required to generate a Login ID."`.
3. WHEN generating an IncrementalNumber, THE **Registration_Service** SHALL query the maximum existing IncrementalNumber for the given CompanyPrefix from the `Persons` table and increment it by 1; if no prior Login_ID exists for that prefix, THE **Registration_Service** SHALL start at `10001`.
4. THE **Registration_Service** SHALL store the generated Login_ID on the `Person.LoginId` field before persisting the record.
5. IF two concurrent registration requests attempt to generate the same Login_ID, THEN THE **Registration_Service** SHALL rely on the unique database index on `Persons.LoginId` to reject the duplicate and return HTTP 409 with the message `"Login ID generation conflict. Please retry."`.
6. THE **Registration_Service** SHALL use the generated Login_ID as both `UserName` and `Email` when creating the ASP.NET Identity user for the Person, so that the existing `FindByEmailAsync`-based login flow resolves the account correctly.

---

### Requirement 3: Person Registration

**User Story:** As a Manager, I want to register a new Person with full personal details and a password, so that the Person has a system identity and can log in.

#### Acceptance Criteria

1. WHEN a `POST /api/persons/register` request is received with valid data, THE **Person_Controller** SHALL invoke the **Registration_Service** to: create a Person record, generate a Login_ID, create an ASP.NET Identity user, and return HTTP 201 with a response body containing: `PersonId`, `LoginId`, `FullName`, `Phone`, `Email`, `Gender`, `DateOfBirth`, `MaritalStatus`, `ProfilePhotoUrl`, `CurrentAddress`, `PermanentAddress`, `IsHired`, and `CreatedDate`.
2. THE **Registration_Service** SHALL accept the following input fields: `FullName` (required, max 150 chars), `CompanyId` (required integer — used to resolve the CompanyPrefix for Login_ID generation), `Phone` (optional, max 50 chars), `Email` (optional, max 150 chars), `Gender` (optional, max 20 chars), `DateOfBirth` (optional, ISO date string), `MaritalStatus` (optional, max 50 chars), `Password` (required, min 6 chars, max 256 chars), `CurrentAddress` (optional object: AddressLine, Country, Province, District, City, PostalCode), `PermanentAddress` (optional object: same fields), `SameAsCurrentAddress` (boolean — when `true`, THE **Registration_Service** SHALL copy all `CurrentAddress` field values into `PermanentAddress` before persisting).
3. IF the supplied `Password` does not satisfy ASP.NET Identity password policy, THEN THE **Person_Controller** SHALL return HTTP 400 with a response body listing each policy violation as returned by `IdentityResult.Errors`.
4. IF the `Email` field is supplied and a Person with the same `Email` already exists in the `Persons` table, THEN THE **Person_Controller** SHALL return HTTP 409 with the message `"A person with this email is already registered."`. IF `Email` is null or empty, this duplicate check SHALL be skipped.
5. WHEN a Person is registered, THE **Registration_Service** SHALL assign the ASP.NET Identity role `"Staff"` to the newly created Identity user, creating the role first if it does not exist.
6. WHEN a Person is registered without a profile photo, THE **Registration_Service** SHALL set `ProfilePhotoUrl` to null; the photo MAY be uploaded separately via `POST /api/persons/{id}/upload-photo`.
7. IF any step in the registration process fails after the Identity user has been created (e.g. database error when saving the Person record), THEN THE **Registration_Service** SHALL delete the newly created Identity user before returning the error response, so that no orphaned Identity accounts are left in the system.

---

### Requirement 4: Person Profile Photo Upload

**User Story:** As a Manager, I want to upload a profile photo for a registered Person, so that the Person's record includes a visual identifier.

#### Acceptance Criteria

1. WHEN a `POST /api/persons/{id}/upload-photo` request is received with a valid image file and a PersonId that exists, THE **Person_Controller** SHALL save the file to `wwwroot/uploads/persons/` with a unique filename, update `Person.ProfilePhotoUrl`, persist the change, and return HTTP 200 with a body containing `photoUrl` (relative path) and `fullUrl` (absolute URL including scheme and host).
2. IF the uploaded file has an extension other than `.jpg`, `.jpeg`, `.png`, or `.webp` (case-insensitive), THEN THE **Person_Controller** SHALL return HTTP 400 with the message `"Only jpg, jpeg, png, webp files are allowed."`.
3. IF the uploaded file size exceeds 5 MB (5 × 1024 × 1024 bytes), THEN THE **Person_Controller** SHALL return HTTP 400 with the message `"File size must be under 5 MB."`.
4. IF the Person already has a profile photo on disk, THEN THE **Person_Controller** SHALL attempt to delete the previous file before saving the new one; if the deletion fails, THE **Person_Controller** SHALL log the failure, overwrite `ProfilePhotoUrl` with the new path, and continue without returning an error.
5. WHEN a `DELETE /api/persons/{id}/photo` request is received and the Person has a profile photo, THE **Person_Controller** SHALL delete the file from disk, set `Person.ProfilePhotoUrl` to null, persist the change, and return HTTP 200 with the message `"Photo removed."`. IF the Person has no photo, THE **Person_Controller** SHALL return HTTP 400 with the message `"No photo to delete."`.
6. IF a `POST /api/persons/{id}/upload-photo` or `DELETE /api/persons/{id}/photo` request is received with a PersonId that does not exist, THEN THE **Person_Controller** SHALL return HTTP 404 with the message `"Person {id} not found."`.

---

### Requirement 5: Person Retrieval

**User Story:** As a Manager, I want to list and retrieve registered Persons, so that I can view the pool of available candidates.

#### Acceptance Criteria

1. WHEN a `GET /api/persons` request is received, THE **Person_Controller** SHALL return HTTP 200 with an array of all Person records; each record SHALL include: `PersonId`, `LoginId`, `FullName`, `Phone`, `Email`, `Gender`, `DateOfBirth`, `MaritalStatus`, `ProfilePhotoUrl`, `IsHired`, `CreatedDate`, and all associated addresses. IF no Persons exist, THE **Person_Controller** SHALL return HTTP 200 with an empty array.
2. WHEN a `GET /api/persons/unassigned` request is received, THE **Person_Controller** SHALL return HTTP 200 with an array of Person records where no Staff record with a non-null `VacancyId` exists for that Person (i.e. `IsHired = false`). IF no unassigned Persons exist, THE **Person_Controller** SHALL return HTTP 200 with an empty array.
3. WHEN a `GET /api/persons/{id}` request is received with a PersonId that exists, THE **Person_Controller** SHALL return HTTP 200 with the full Person record including: `PersonId`, `LoginId`, `FullName`, `Phone`, `Email`, `Gender`, `DateOfBirth`, `MaritalStatus`, `ProfilePhotoUrl`, `IsHired`, `CreatedDate`, and all associated addresses (zero, one, or two address objects depending on what was saved).
4. IF a `GET /api/persons/{id}` request is received with a PersonId that does not exist, THEN THE **Person_Controller** SHALL return HTTP 404 with the message `"Person {id} not found."`.

---

### Requirement 6: Person Update and Deletion

**User Story:** As a Manager, I want to update or remove a Person's details, so that the registry stays accurate.

#### Acceptance Criteria

1. WHEN a `PUT /api/persons/{id}` request is received with valid data, THE **Person_Controller** SHALL update the following Person fields: `FullName`, `Phone`, `Email`, `Gender`, `DateOfBirth`, `MaritalStatus`; fully replace the `Current` address record if provided; fully replace the `Permanent` address record if provided; and return HTTP 200 with the updated Person record in the same shape as the GET response.
2. IF a `PUT /api/persons/{id}` request is received for a PersonId that does not exist, THEN THE **Person_Controller** SHALL return HTTP 404 with the message `"Person {id} not found."`.
3. WHEN a `DELETE /api/persons/{id}` request is received for a Person who is not currently hired, THE **Person_Controller** SHALL: delete the profile photo file from disk if `ProfilePhotoUrl` is not null; delete the associated `PersonAddresses` rows (via cascade); delete the `Person` record; delete the linked ASP.NET Identity user; and return HTTP 200 with the message `"Person '{FullName}' removed."`.
4. IF a `DELETE /api/persons/{id}` request targets a Person who is currently hired (a Staff record with a non-null `VacancyId` exists for this Person), THEN THE **Person_Controller** SHALL return HTTP 409 with the message `"Cannot delete a Person who is currently assigned to a vacancy."`.
5. IF a `DELETE /api/persons/{id}` request is received for a PersonId that does not exist, THEN THE **Person_Controller** SHALL return HTTP 404 with the message `"Person {id} not found."`.

---

### Requirement 7: Hire Person into Vacancy

**User Story:** As a Manager, I want to hire a registered, unassigned Person into a vacant position, so that the vacancy is filled and the Person becomes an active employee.

#### Acceptance Criteria

1. WHEN a `POST /api/staff/hire-person` request is received with a valid `PersonId` referencing an unassigned Person and a valid `VacancyId` referencing an unfilled Vacancy, THE **Hire_Service** SHALL create a Staff record with `PersonId`, `FullName` (copied from Person), `Email` (copied from Person), `Phone` (copied from Person), `VacancyId`, and `JoiningDate`; then return HTTP 201 with the created Staff record in the same shape as the existing Staff GET response.
2. WHEN recording the `JoiningDate`, THE **Hire_Service** SHALL use the `JoiningDate` value supplied in the request body if it is provided and is a valid UTC date not earlier than `Vacancy.CreatedDate` and not later than `DateTime.UtcNow`; otherwise THE **Hire_Service** SHALL default to `DateTime.UtcNow`.
3. WHEN a Person is hired, THE **Hire_Service** SHALL set `Vacancy.IsFilled = true` in the same database transaction as the Staff record creation, so that both changes are committed atomically.
4. IF the supplied `VacancyId` does not exist, THEN THE **Hire_Service** SHALL return HTTP 404 with the message `"Vacancy {vacancyId} not found."`.
5. IF the Vacancy identified by `VacancyId` has `IsFilled = true`, THEN THE **Hire_Service** SHALL return HTTP 400 with the message `"Vacancy '{VacancyCode}' is already filled."`.
6. IF the supplied `PersonId` does not exist, THEN THE **Hire_Service** SHALL return HTTP 404 with the message `"Person {personId} not found."`.
7. IF the Person identified by `PersonId` already has an associated Staff record with a non-null `VacancyId`, THEN THE **Hire_Service** SHALL return HTTP 400 with the message `"Person '{FullName}' is already assigned to a vacancy."`.

---

### Requirement 8: Legacy Hire Endpoint Compatibility

**User Story:** As a Developer, I want the existing `POST /api/staff/hire/{vacancyId}` endpoint to remain functional, so that existing integrations are not broken during the transition.

#### Acceptance Criteria

1. WHEN a `POST /api/staff/hire/{vacancyId}` request is received, THE **Staff_Controller** SHALL continue to accept `HireStaffDto` (FullName, Email, Phone), create a Staff record with `PersonId = null`, and return HTTP 201 as before. Any `PersonId` field present in the request body SHALL be silently ignored.
2. THE **Staff_Controller** SHALL set `Staff.PersonId = null` on all legacy-created Staff records so that they are distinguishable from Person-linked hires.
3. WHEN a `POST /api/staff/hire-person` request is received, THE **Staff_Controller** SHALL require a `PersonId` (GUID, required) and a `VacancyId` (GUID, required) in the request body; IF either field is missing or not a valid GUID, THE **Staff_Controller** SHALL return HTTP 400 with a validation error message. THE **Staff_Controller** SHALL NOT accept `FullName`, `Email`, or `Phone` fields on this endpoint.
4. IF the `VacancyId` supplied to `POST /api/staff/hire/{vacancyId}` does not exist or the Vacancy is already filled, THEN THE **Staff_Controller** SHALL return the same HTTP 404 or HTTP 400 responses as the existing implementation.

---

### Requirement 9: Person Login

**User Story:** As a registered Person, I want to log in using my Login ID and password, so that I can access the system.

#### Acceptance Criteria

1. WHEN a `POST /api/auth/login` request is received with `Email` set to a valid Login_ID and a password that matches the stored credential for the resolved Identity user, THE **AuthController** SHALL return HTTP 200 with a body containing `Success = true`, `Email` (the Login_ID), and `Roles` (array of role names assigned to the user).
2. IF the value supplied in the `Email` field does not match any `IdentityUser.Email` in the system, OR if the password does not match the stored credential, THEN THE **AuthController** SHALL return HTTP 401 with `Success = false` and `Message = "Invalid email or password."`.
3. IF the Identity account for the Login_ID is locked out, THEN THE **AuthController** SHALL return HTTP 423 with `Success = false` and `Message = "Account is locked out."`.
4. IF the `POST /api/auth/login` request body is missing required fields or is malformed, THEN THE **AuthController** SHALL return HTTP 400 with the model validation errors.
5. WHEN a Person is registered, THE **Registration_Service** SHALL set both `IdentityUser.UserName` and `IdentityUser.Email` to the generated Login_ID, so that the existing `SignInManager.PasswordSignInAsync(email, ...)` and `UserManager.FindByEmailAsync(email)` calls in `AuthController.Login` resolve the Person's account using the Login_ID as the credential.
