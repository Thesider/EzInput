# Report 1: Project Proposal Report (Initiation Phase)

## 1. Project Title

**EZ-Input: Smart Document Management and Data Entry System (with OCR and Speech-to-Text)**

## 2. Objectives and Expected Outcomes

### Objectives

- Optimize and automate document data entry workflows.
- Reduce manual typing effort by supporting image-based text extraction and voice-based transcription.
- Provide a reliable document lifecycle from creation to storage and retrieval.

### Expected Outcomes

A functional application with standard CRUD capabilities:

- **Create**: Create new documents by:
  - Uploading images (invoices, book pages, business cards) and extracting printed text using OCR.
  - Recording voice and converting speech to text.
  - Entering text manually.
- **Read**: View a document dashboard and search documents by keywords.
- **Update**: Edit extracted/transcribed text before final save to correct recognition errors.
- **Delete**: Remove obsolete documents.

## 3. Scope and Technical Requirements

### Scope

- Supported languages: **English** and **Vietnamese**.
- OCR scope: **printed text only** (handwriting excluded in this phase).
- UX target: intuitive, low-distraction interface optimized for fast data entry.

### Technical Requirements

- **Frontend**: ASP.NET Core MVC with Razor Views, HTML, CSS, JavaScript.
- **Backend**: C# with .NET 8.
- **Data processing (optional microservice)**: Python for advanced OCR/audio preprocessing and AI integration when needed.
- **Database**: Microsoft SQL Server.
- **Core technologies**:
  - OCR (Tesseract + OpenCV pipeline)
  - Speech-to-Text (Microsoft Speech APIs)

## 4. Initial Implementation Plan

### Phase 1: Project Setup and Architecture

- Initialize GitHub repository and enforce branch protections.
- Finalize layered architecture and project boundaries.
- Define SQL schema and create initial EF Core migrations.
- Establish baseline ASP.NET MVC app routing and shared UI layout.

### Phase 2: Core CRUD and Authentication

- Implement authentication and authorization using ASP.NET Core Identity.
- Build document CRUD services and controllers.
- Integrate database persistence with EF Core repositories.
- Connect UI forms to backend for manual text entry and document management.

### Phase 3: AI Feature Integration

- Add image upload and OCR extraction workflow.
- Add browser audio recording and speech-to-text workflow.
- Implement extraction status tracking and error reporting.
- Add post-processing editor for user correction before save.

### Phase 4: Testing, Refinement, and Deployment

- Execute end-to-end tests (upload/record -> extract/transcribe -> edit -> save).
- Improve usability with loading states, retry flows, and validation feedback.
- Deploy application and verify production configuration.

## 5. Resources and Tools

### Software, Frameworks, and Libraries

- **Frontend**:
  - ASP.NET Core MVC + Razor
  - Bootstrap (current baseline) and optional Tailwind CSS for custom design acceleration
- **Backend and database**:
  - .NET 8 (ASP.NET Core)
  - Entity Framework Core
  - SQL Server
- **AI/media processing**:
  - OCR: Tesseract, OpenCV
  - Speech-to-Text: Microsoft Speech APIs
- **DevOps and quality**:
  - Swagger (API documentation for integration endpoints)
  - `.gitignore` policies for build artifacts and secret files

### Environment Preparation

- Use .NET User Secrets for sensitive backend configuration.
- Use environment-specific configuration for API keys/connection strings.
- Add request-size and duration guards for large uploads and long recordings.

## 6. Risk Assessment

### Key Risks

- **AI accuracy risk**: OCR degrades with blurry/low-light images; speech quality degrades with noise, accent, or fast speech.
- **Cost and performance risk**: media processing may increase latency; paid services can exceed budget in testing.
- **Integration risk**: multi-component architecture (UI, backend, AI pipeline, storage) increases integration complexity.

### Mitigation Strategies

- **Human-in-the-loop validation**: always require user review and correction before final save.
- **Input constraints**: image compression and recording duration limits (for example, max 2 minutes).
- **Cost controls**: use free tiers/dev quotas and monitor usage.
- **Operational controls**: implement retries, timeouts, and structured error responses.

## 7. References (Optional)

- Microsoft Learn: ASP.NET Core, EF Core, and SQL Server integration guidance.
- Tesseract OCR documentation.
- OpenCV official documentation.
- Microsoft Speech service documentation.
