using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class livestream_aggregates_can_be_explicit: OneOffConfigurationsContext
{
    [Fact]
    public void livestream_aggregate_projections_should_also_be_allowed_to_be_explicit()
    {
        var store = StoreOptions(opts =>
        {
            opts.Projections.Add(new PatientProjection(), ProjectionLifecycle.Live);
        });

        theStore.StorageFeatures.AllDocumentMappings.Select(x => x.DocumentType)
            .ShouldContain(typeof(Patient));
    }

    [Fact]
    public async Task explicit_livestream_aggregate_Can_Be_Created()
    {
        var store = StoreOptions(opts =>
        {
            opts.Projections.Add(new PatientProjection(), ProjectionLifecycle.Live);
        });
        var patientId = Guid.NewGuid();
        var patientRegistered = new PatientRegistered(
            PatientId: Guid.NewGuid(),
            Name: "John Doe",
            DateOfBirth: new(1980, 5, 20));

        var appointmentScheduled = new AppointmentScheduled(
            AppointmentId: Guid.NewGuid(),
            PatientId: patientRegistered.PatientId,
            AppointmentDate: DateTime.Now.AddDays(7)); // Schedule an appointment for 7 days from now

        var treatmentAdministered = new TreatmentAdministered(
            TreatmentId: Guid.NewGuid(),
            PatientId: patientRegistered.PatientId,
            TreatmentDescription: "Routine Checkup",
            TreatmentDate: appointmentScheduled.AppointmentDate.AddHours(1)); // Assume the treatment is an hour after the appointment

        var patientDischarged = new PatientDischarged(
            PatientId: patientRegistered.PatientId,
            DischargeDate: treatmentAdministered.TreatmentDate.AddDays(1)); // Discharged the day after treatment

        await using var session = store.LightweightSession();
        session.Events.StartStream<Patient>(patientId, patientRegistered, appointmentScheduled, treatmentAdministered, patientDischarged);
        await session.SaveChangesAsync();

        var patient = await session.Events.AggregateStreamAsync<Patient>(patientId);
        patient.ShouldNotBeNull();
        patient.Name.ShouldBe("John Doe");
        patient.DateOfBirth.ShouldBe(new(1980, 5, 20));
        patient.Appointments.Count.ShouldBe(1);
        patient.Appointments[0].AppointmentDate.ShouldBe(appointmentScheduled.AppointmentDate);
        patient.Treatments.Count.ShouldBe(1);
        patient.Treatments[0].Description.ShouldBe("Routine Checkup");
        patient.Treatments[0].TreatmentDate.ShouldBe(treatmentAdministered.TreatmentDate);
        patient.DischargeDate.ShouldBe(patientDischarged.DischargeDate);
    }

    public class PatientProjection: CustomProjection<Patient, Guid>
    {
        public PatientProjection()
        {
            // Define how to group events into aggregates by stream ID
            Slicer = new ByStreamId<Patient>();

            // Optimize event filtering for the async daemon
            IncludeType<PatientRegistered>();
            IncludeType<AppointmentScheduled>();
            IncludeType<TreatmentAdministered>();
            IncludeType<PatientDischarged>();
        }

        public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<Patient, Guid> slice, CancellationToken cancellation, ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
        {
            var aggregate = slice.Aggregate;

            foreach (var e in slice.AllData())
            {
                aggregate = e switch
                {
                    PatientRegistered registered => Patient.Create(registered),
                    AppointmentScheduled scheduled => Patient.Apply(aggregate, scheduled),
                    TreatmentAdministered administered => Patient.Apply(aggregate, administered),
                    PatientDischarged discharged => Patient.Apply(aggregate, discharged),
                    _ => aggregate
                };
            }

            if (aggregate is { })
            {
                session.Store(aggregate);
            }

            return new();
        }
    }


    public record PatientRegistered(Guid PatientId, string Name, DateTime DateOfBirth);
    public record AppointmentScheduled(Guid AppointmentId, Guid PatientId, DateTime AppointmentDate);
    public record TreatmentAdministered(Guid TreatmentId, Guid PatientId, string TreatmentDescription, DateTime TreatmentDate);
    public record PatientDischarged(Guid PatientId, DateTime DischargeDate);

    public record Patient(
        Guid PatientId,
        string Name,
        DateTime DateOfBirth,
        List<Appointment> Appointments,
        List<Treatment> Treatments,
        DateTime? DischargeDate)
    {

        public static Patient Create(PatientRegistered @event) =>
            new(@event.PatientId, @event.Name, @event.DateOfBirth, [], [], null);

        public static Patient Apply(Patient state, AppointmentScheduled @event) =>
            state with
            {
                Appointments = [.. state.Appointments, new(@event.AppointmentId, @event.AppointmentDate)]
            };

        public static Patient Apply(Patient state, TreatmentAdministered @event) =>
            state with
            {
                Treatments = [.. state.Treatments, new(@event.TreatmentId, @event.TreatmentDescription, @event.TreatmentDate)]
            };

        public static Patient Apply(Patient state, PatientDischarged @event) =>
            state with
            {
                DischargeDate = @event.DischargeDate
            };
    }

    public record Appointment(Guid AppointmentId, DateTime AppointmentDate);

    public record Treatment(Guid TreatmentId, string Description, DateTime TreatmentDate);
}
