using EchidnaJav.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Infrastructure.Persistence
{
    using Microsoft.EntityFrameworkCore;

    public class AppDbContext : DbContext
    {
        public DbSet<Movie> Movies { get; set; }
        public DbSet<Actress> Actresses { get; set; }
        public DbSet<Genre> Genres { get; set; }
        public DbSet<FileEntry> Files { get; set; }
        public DbSet<MovieActress> MovieActresses { get; set; }
        public DbSet<MovieGenre> MovieGenres { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<MovieGenre>()
                .HasOne(mg => mg.Movie)
                .WithMany(m => m.MovieGenres)
                .HasForeignKey(mg => mg.MovieId);

            modelBuilder.Entity<MovieGenre>()
                .HasOne(mg => mg.Genre)
                .WithMany()
                .HasForeignKey(mg => mg.GenreId);

            modelBuilder.Entity<MovieActress>()
                .HasOne(ma => ma.Movie)
                .WithMany(m => m.MovieActresses)
                .HasForeignKey(ma => ma.MovieId);

            modelBuilder.Entity<MovieActress>()
                .HasOne(ma => ma.Actress)
                .WithMany(a => a.MovieActresses)
                .HasForeignKey(ma => ma.ActressId);

            modelBuilder.Entity<MovieActress>()
                .HasKey(ma => new { ma.MovieId, ma.ActressId });

            modelBuilder.Entity<MovieGenre>()
                .HasKey(mg => new { mg.MovieId, mg.GenreId });

            modelBuilder.Entity<FileEntry>()
                .HasIndex(f => f.Hash);

            modelBuilder.Entity<FileEntry>()
                .HasIndex(f => f.FilePath);

            modelBuilder.Entity<Genre>()
                .HasIndex(g => g.Name)
                .IsUnique();
        }
    }
}
